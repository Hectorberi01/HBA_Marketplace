using System.Net.Http.Headers;
using HBA.Tests.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Security.Cryptography;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace HBA.Catalog.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CATALOG CONTRE SES VRAIES DÉPENDANCES — POSTGRES ET KAFKA, EN CONTENEURS.
///
/// CE NIVEAU N'EXISTAIT PAS. `tests/integration` NE CONTENAIT QU'UN README.
///
/// Son propre texte disait pourtant ce qu'il manquait : « C'est le niveau qui
/// remplace ce qu'un test unitaire garantissait gratuitement dans le monolithe :
/// qu'un événement publié est bien reçu. » Dans un seul processus, publier et
/// consommer étaient un appel de méthode. Découpés, ce sont une table, un
/// courtier, une sérialisation et un abonnement — quatre endroits où le lien peut
/// se rompre sans qu'aucun test unitaire ne s'en aperçoive.
///
/// CE QUE CETTE FIXTURE ÉPROUVE SANS ÉCRIRE UN SEUL TEST POUR CELA.
///
/// `Database__MigrateOnStartup=true` sur une base VIDE : le service applique
/// lui-même toutes ses migrations, dans l'ordre, à froid. L'audit du catalogue
/// notait que ce départ à froid n'avait jamais été rejoué — `check-migrations.py`
/// le simule en lisant les fichiers, il ne l'exécute pas. Si une migration est
/// incohérente, la fixture échoue au démarrage et TOUS les tests tombent
/// ensemble : c'est un signal net, sur la bonne cause.
///
/// POURQUOI UNE FIXTURE DE COLLECTION ET NON UNE PAR CLASSE.
///
/// Démarrer Postgres et Kafka coûte quelques dizaines de secondes. Par classe de
/// test, la suite deviendrait assez lente pour qu'on cesse de la lancer — et une
/// suite qu'on ne lance pas ne vaut rien. Les tests doivent donc rester
/// INDÉPENDANTS de l'ordre : chacun crée ses propres données.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CatalogIntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hba_catalog")
        .WithUsername("hba")
        .WithPassword("hba")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.6.1")
        .Build();

    /// <summary>Adresse du courtier, pour qu'un test puisse produire un message lui-même.</summary>
    public string BootstrapServers => _kafka.GetBootstrapAddress();

    /// <summary>Chaîne de connexion à la base du service, pour vérifier un effet en table.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// LES VARIABLES D'ENVIRONNEMENT SONT POSÉES ICI, PAS DANS UN CONSTRUCTEUR
    ///    STATIQUE.
    ///
    /// `AuthorizationTestFactory` les pose statiquement, parce que ses valeurs sont
    /// des constantes. Les nôtres n'existent pas avant que les conteneurs n'aient
    /// démarré : les ports sont attribués dynamiquement.
    ///
    /// L'ordre tient parce que `WebApplicationFactory` construit son hôte
    /// PARESSEUSEMENT, au premier `CreateClient()`. xUnit appelle
    /// `InitializeAsync` avant le premier test, donc avant ce premier client. Un
    /// hôte construit dans cette méthode — ou un `CreateClient()` prématuré —
    /// casserait l'ordre et lirait les anciennes valeurs.
    ///
    /// Et ce doit être des VARIABLES D'ENVIRONNEMENT : les services utilisent
    /// l'hébergement minimal et lisent leur configuration dès la deuxième ligne de
    /// `Program.Main`, bien avant que les rappels de `ConfigureAppConfiguration` ne
    /// s'exécutent. Le piège est documenté en détail dans `AuthorizationTestFactory`.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        static void Poser(string cle, string valeur)
            => Environment.SetEnvironmentVariable(cle, valeur);

        Poser("ASPNETCORE_ENVIRONMENT", "Testing");
        Poser("SERVICE_NAME", "catalog-service");

        Poser("ConnectionStrings__Default", _postgres.GetConnectionString());

        // VRAI, CONTRAIREMENT AUX TESTS D'AUTORISATION. C'est tout l'objet de
        // ce niveau : le schéma est construit par les migrations réelles, sur une
        // base vide, à chaque exécution.
        Poser("Database__MigrateOnStartup", "true");

        Poser("Kafka__Enabled", "true");
        Poser("Kafka__BootstrapServers", _kafka.GetBootstrapAddress());

        // L'outbox doit tourner : sans elle, les événements restent en table et le
        // test de bout en bout n'éprouve que la moitié du chemin.
        Poser("OUTBOX_ENABLED", "true");

        Poser("Authentication__SigningKey", TestTokens.SigningKey);
        Poser("Authentication__Issuer", TestTokens.Issuer);
        Poser("Authentication__Audience", TestTokens.Audience);
        Poser("Internal__ApiKey", "cle-interne-de-test");

        // ═════════════════════════════════════════════════════════════════
        // UNE VRAIE PAIRE DE CLÉS, ET NON LE MODE NON SIGNÉ.
        //
        // Premier essai : `Internal__IdentitesNonSignees=true`, en supposant un
        // hôte de test en `Development`. IL NE L'EST PAS, et la raison est écrite
        // quelques lignes plus haut, dans ce fichier : `ASPNETCORE_ENVIRONMENT`
        // vaut délibérément `Testing` — ni `Development`, qui migrerait la base,
        // ni `Production`, que `PaymentsModuleInstaller` sanctionne.
        //
        // Les 253 tests tombaient donc tous sur le garde de `AddHbaGrpc`, qui a
        // fait exactement son travail. Le raccourci supposait un environnement
        // que le réglage voisin interdit ; le supposer plutôt que le lire est
        // précisément l'erreur.
        //
        // Engendrer la paire coûte environ une milliseconde par hôte de test, et
        // rapporte davantage que le raccourci qu'elle remplace : le chemin
        // RÉELLEMENT exécuté en production — frappe, signature, vérification,
        // table d'autorisations — est celui que les tests empruntent.
        //
        // LE NOM EST CELUI DE L'ASSEMBLY DU POINT D'ENTRÉE, PAS DE L'ASSEMBLY
        //    DE TEST.
        //
        // À défaut de `Internal:ServiceName`, l'identité se replie sur
        // `Assembly.GetEntryAssembly()` — sous xUnit, l'assembly de TEST, absente
        // de `AutorisationsGrpc`. Tout appel gRPC partirait en `PermissionDenied`,
        // et l'erreur désignerait la table plutôt que le repli qui l'a produite.
        //
        // Le registre ne contient que cet hôte : il n'y a pas d'autre appelant à
        // vérifier en test, et une entrée de plus n'attesterait rien.
        // ═════════════════════════════════════════════════════════════════
        var nomDeLHote = typeof(Program).Assembly.GetName().Name!;

        using (var identite = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Poser("Internal__ServiceName", nomDeLHote);
            Poser("Internal__PrivateKey",
                Convert.ToBase64String(identite.ExportPkcs8PrivateKey()));
            Poser("Internal__PublicKeys",
                $"{nomDeLHote}={Convert.ToBase64String(identite.ExportSubjectPublicKeyInfo())}");
        }

        // ADRESSES SYNTAXIQUEMENT VALIDES VERS DES PORTS FERMÉS.
        //
        // `AddMerchantsGrpcClient` et `AddMediaGrpcClient` lèvent à la CONSTRUCTION
        // de l'hôte si l'adresse manque — pas au premier appel. Elles doivent donc
        // exister ; elles n'ont pas à répondre. Les tests qui traversent une garde
        // dépendant d'un voisin substituent le client, ils ne l'appellent pas.
        Poser("Services__Merchant", "http://127.0.0.1:59107");
        Poser("Services__Media", "http://127.0.0.1:59110");
        Poser("Services__Inventory", "http://127.0.0.1:59104");
        Poser("Services__Identity", "http://127.0.0.1:59101");

        Poser("Redis__ConnectionString", string.Empty);

        // L'EXPORT DE TÉLÉMÉTRIE RESTE COUPÉ, L'INSTRUMENTATION NON.
        //
        // Aucun collecteur ne tourne ici : une adresse ferait réessayer
        // l'exportateur en tâche de fond et noierait la sortie des tests sous des
        // erreurs de connexion sans rapport. Les `Activity` continuent d'être
        // créées — c'est ce qui compte pour le test de propagation.
        Poser("OpenTelemetry__Endpoint", string.Empty);

        // SANS CETTE LIGNE, `La_documentation_openapi_est_servie_sans_jeton`
        //    ÉCHOUE — ET IL N'AVAIT JAMAIS ÉTÉ EXÉCUTÉ POUR LE DIRE.
        //
        // `UseHbaOpenApi` n'ouvre la page que si `OpenApi:Enabled` le dit, ou à
        // défaut en Development. Cette fixture tourne en `Testing` : la page était
        // donc fermée, et le test aurait rendu 404 sur `/swagger/v1/swagger.json`.
        //
        // Il ne s'en est jamais aperçu parce qu'il porte `[Trait("Docker","true")]`
        // et que `make test` filtre sur `Docker!=true` : le seul chemin qui
        // l'exécute est `make test-integration`. C'est le prix du découpage — et la
        // raison pour laquelle cette cible doit être lancée, pas seulement écrite.
        Poser("OpenApi__Enabled", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Testing");

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    /// <summary>Un client porteur d'un jeton forgé.</summary>
    public HttpClient CreateClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>
/// La collection xUnit qui partage la fixture.
///
/// SANS CET ATTRIBUT SUR CHAQUE CLASSE DE TEST, xUnit LES EXÉCUTE EN PARALLÈLE
/// et démarre une paire de conteneurs par classe. La suite passerait quand même —
/// simplement quatre fois plus lentement, et personne ne comprendrait pourquoi.
/// </summary>
[CollectionDefinition(Nom)]
public sealed class CatalogIntegrationCollection : ICollectionFixture<CatalogIntegrationFixture>
{
    public const string Nom = "catalog-integration";
}
