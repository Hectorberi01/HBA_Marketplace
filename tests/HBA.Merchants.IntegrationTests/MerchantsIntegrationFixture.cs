using System.Net.Http.Headers;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using HBA.Identity.Contracts;
using HBA.Inventory.Contracts;
using HBA.Media.Contracts;
using HBA.Ordering.Contracts;
using HBA.Tests.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// SELLER-SERVICE CONTRE SES VRAIES DÉPENDANCES — POSTGRES ET KAFKA, EN CONTENEURS.
///
/// CE SERVICE N'AVAIT AUCUN TEST D'INTÉGRATION, ET C'EST CELUI QUI EN AVAIT
///    LE PLUS BESOIN.
///
/// Les 63 cas de `HBA.Merchants.UnitTests` éprouvent l'agrégat en mémoire, et le
/// font bien — c'est eux qui ont montré que la bascule KYB dépréciée devançait le
/// geste explicite partout. Mais ils ne disent rien de ce que ce lot vérifie :
///
///   • que les NEUF migrations s'appliquent à froid, dans l'ordre, sur une base
///     vide. `check-migrations.py` les LIT, il ne les exécute pas — et deux
///     d'entre elles datent de cette session ;
///   • que la garde d'inbox du consommateur RGPD tient contre un vrai courtier.
///     C'est le SEUL consommateur du dépôt sans idempotence naturelle : chez lui,
///     un rejeu se voit dans les données, pas seulement dans une ligne de trace ;
///   • que les événements traversent réellement outbox → Kafka. Un test unitaire
///     s'arrête au `Raise()` ; entre lui et le message publié, il y a une
///     sérialisation, une table, un processeur et un nom de sujet — quatre
///     endroits où le lien se rompt sans casser la compilation.
///
/// POURQUOI UNE FIXTURE DE COLLECTION ET NON UNE PAR CLASSE.
///
/// Démarrer Postgres et Kafka coûte quelques dizaines de secondes. Par classe, la
/// suite deviendrait assez lente pour qu'on cesse de la lancer — et une suite
/// qu'on ne lance pas ne vaut rien. Les tests doivent donc rester INDÉPENDANTS de
/// l'ordre : chacun crée son propre vendeur, avec son propre compte.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MerchantsIntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hba_sellers")
        .WithUsername("hba")
        .WithPassword("hba")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.6.1")
        .Build();

    /// <summary>Adresse du courtier, pour qu'un test produise ou lise lui-même.</summary>
    public string BootstrapServers => _kafka.GetBootstrapAddress();

    /// <summary>Chaîne de connexion à la base du service, pour vérifier un effet en table.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// LES VARIABLES D'ENVIRONNEMENT SONT POSÉES ICI, PAS DANS UN CONSTRUCTEUR
    ///    STATIQUE.
    ///
    /// Les ports des conteneurs sont attribués dynamiquement : ces valeurs
    /// n'existent pas avant `StartAsync`. L'ordre tient parce que
    /// `WebApplicationFactory` construit son hôte PARESSEUSEMENT, au premier
    /// `CreateClient()`, et que xUnit appelle `InitializeAsync` avant le premier
    /// test. Un `CreateClient()` prématuré — dans cette méthode, par exemple —
    /// lirait les anciennes valeurs.
    ///
    /// Et ce doit être des VARIABLES D'ENVIRONNEMENT : le socle lit sa
    /// configuration dès la deuxième ligne de `Program`, bien avant que les
    /// rappels de `ConfigureAppConfiguration` ne s'exécutent.
    /// </summary>
    /// <summary>
    /// Crée les sujets AVANT que l'hôte ne s'abonne.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SANS CELA, LE CONSOMMATEUR EST ABONNÉ À DES SUJETS QUI N'EXISTENT PAS.
    ///
    /// Un sujet Kafka naît à la première publication. Les tests qui injectent un
    /// événement — anonymisation RGPD, note recalculée, commande confirmée — créent
    /// donc `service.identity.v1`, `service.engagement.v1` et `service.order.v1`
    /// APRÈS que l'hôte s'y est abonné. librdkafka ne redemande la liste des sujets
    /// qu'à intervalle régulier : entre-temps, le service est abonné à un sujet
    /// qu'il ne voit pas, ne consomme rien, et ne journalise rien d'anormal.
    ///
    /// Le symptôme est une attente de soixante secondes qui expire sur une note
    /// restée à zéro — et l'on cherche une faute de nommage là où il n'y en a pas.
    ///
    /// C'EST AUSSI CE QUE FAIT LA PRODUCTION. Les sujets y sont provisionnés par
    /// `k8s/overlays/*/kafka-topics.yaml`, avec leurs partitions et leur rétention ;
    /// aucun service ne les crée à la volée. Les provisionner ici rapproche le test
    /// du réel au lieu de l'en éloigner.
    ///
    /// Une partition et un facteur de réplication de 1 : un courtier unique en
    /// conteneur, et l'ordre par clé est de toute façon garanti dans une partition.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private async Task ProvisionnerLesSujetsAsync()
    {
        string[] sujets =
        [
            "service.merchant.v1",
            "service.identity.v1",
            "service.engagement.v1",
            "service.order.v1"
        ];

        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build();

        try
        {
            await admin.CreateTopicsAsync(sujets.Select(nom => new TopicSpecification
            {
                Name = nom,
                NumPartitions = 1,
                ReplicationFactor = 1
            }));
        }
        catch (CreateTopicsException ex) when (
            ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // Déjà là : le courtier est réutilisé entre deux classes de tests.
        }
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        await ProvisionnerLesSujetsAsync();

        static void Poser(string cle, string valeur)
            => Environment.SetEnvironmentVariable(cle, valeur);

        Poser("ASPNETCORE_ENVIRONMENT", "Testing");

        // ═════════════════════════════════════════════════════════════════════
        // `seller-service`, LA VALEUR RÉELLE DE `docker-compose.dev.yml`.
        //
        // Cette ligne posait `merchant-service` en affirmant que c'était la valeur
        // du compose. Ce n'était plus vrai. Le test corrigeait donc, à son insu, le
        // défaut central d'ISSUE-001 : les producteurs dérivaient leur sujet du nom
        // du conteneur — `service.seller.v1` — quand les consommateurs écoutaient
        // `service.merchant.v1`. La suite passait au vert sur une plateforme qui,
        // déployée, n'échangeait rien.
        //
        // Un harnais qui s'écarte de la production ne prouve que lui-même. On pose
        // désormais la vraie valeur, et c'est `HbaTopics` qui traduit
        // `seller-service` → domaine `merchant`. Si quelqu'un retirait cette
        // traduction, ce test tomberait — c'est exactement ce qu'on lui demande.
        // ═════════════════════════════════════════════════════════════════════
        Poser("SERVICE_NAME", "seller-service");

        Poser("ConnectionStrings__Default", _postgres.GetConnectionString());

        // VRAI, CONTRAIREMENT AUX TESTS D'AUTORISATION. C'est tout l'objet de ce
        // niveau : le schéma est construit par les migrations réelles, sur une base
        // vide, à chaque exécution.
        Poser("Database__MigrateOnStartup", "true");

        Poser("Kafka__Enabled", "true");
        Poser("Kafka__BootstrapServers", _kafka.GetBootstrapAddress());

        // Sans l'outbox, les événements restent en table et le parcours de bout en
        // bout n'éprouve que la moitié du chemin.
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

        // ADRESSE SYNTAXIQUEMENT VALIDE VERS UN PORT FERMÉ.
        //
        // `AddIdentityGrpcClient` lève à la CONSTRUCTION de l'hôte si
        // `Services:Identity` manque — pas au premier appel. Elle doit donc
        // exister ; elle n'a pas à répondre, puisque `IIdentityModuleApi` est
        // substitué ci-dessous.
        Poser("Services__Identity", "http://127.0.0.1:59101");

        // Même raison qu'au-dessus : `AddMediaGrpcClient` et `AddInventoryGrpcClient`
        // lèvent à la construction de l'hôte si l'adresse manque. Les deux contrats
        // sont substitués plus bas, donc ces ports n'ont pas à répondre.
        Poser("Services__Media", "http://127.0.0.1:59110");
        Poser("Services__Inventory", "http://127.0.0.1:59104");
        Poser("Services__Order", "http://127.0.0.1:59106");

        Poser("Redis__ConnectionString", string.Empty);

        // L'EXPORT DE TÉLÉMÉTRIE RESTE COUPÉ, L'INSTRUMENTATION NON.
        //
        // Aucun collecteur ne tourne ici : une adresse ferait réessayer
        // l'exportateur en tâche de fond et noierait la sortie des tests sous des
        // erreurs de connexion sans rapport.
        Poser("OpenTelemetry__Endpoint", string.Empty);

        // SANS CETTE LIGNE, LA DOCUMENTATION N'EST PAS SERVIE ICI.
        //
        // `UseHbaOpenApi` n'ouvre la page que si `OpenApi:Enabled` le dit, ou à
        // défaut en Development — et cette fixture tourne en `Testing`. Le test qui
        // fige l'ordre du pipeline échouerait donc sur une page absente, en
        // laissant croire à une régression du middleware.
        Poser("OpenApi__Enabled", "true");
    }

    /// <summary>
    /// Le service média en mémoire, que les tests remplissent avant de rattacher
    /// une pièce KYB. Voir <see cref="MediaDeTest"/>.
    /// </summary>
    /// <remarks>
    /// `internal` ET NON `public` : la fixture est publique parce que xUnit
    /// l'exige, mais `MediaDeTest` reste interne — c'est un faux, il n'a rien à
    /// faire dans la surface publique de l'assembly. Les classes de test vivent
    /// dans le même assembly et y accèdent sans difficulté.
    /// </remarks>
    internal MediaDeTest Media { get; } = new();

    /// <summary>
    /// L'inventaire en mémoire, que les tests remplissent avant de rattacher un
    /// lieu d'expédition. Voir <see cref="InventaireDeTest"/>.
    /// </summary>
    internal InventaireDeTest Inventaire { get; } = new();

    /// <summary>
    /// order-service en mémoire, pour le compteur de ventes. Voir
    /// <see cref="CommandesDeTest"/> : le gestionnaire REDEMANDE le total plutôt
    /// que de lire l'événement, et c'est cette réponse-là que les tests pilotent.
    /// </summary>
    internal CommandesDeTest Commandes { get; } = new();

    /// <summary>
    /// QUATRE VOISINS SUBSTITUÉS, ET AUCUN AUTRE.
    ///
    /// **Identity** : `RegisterSellerCommandHandler` l'appelle de façon SYNCHRONE
    /// avant d'inscrire quoi que ce soit — c'est délibéré (accepter une inscription
    /// avant de savoir si le compte existe serait pire). Faire tourner un vrai
    /// identity-service en conteneur ferait de chaque test de cette suite un test
    /// de DEUX services : une migration cassée chez le voisin ferait échouer le
    /// parcours KYB, et l'on chercherait ici une panne qui n'y est pas.
    ///
    /// **Média** : `AddKybDocumentCommandHandler` lui demande à qui appartient un
    /// fichier avant de le rattacher. Contrairement à Identity, ce faux-ci est
    /// PILOTABLE — la règle à éprouver est justement le refus, et un faux qui
    /// dirait toujours oui rendrait vert un service ayant perdu son contrôle de
    /// propriété. Singleton : le test dépose, la requête lit.
    ///
    /// **Inventaire** : même chose pour le lieu d'expédition d'une boutique, dont
    /// l'appartenance était elle aussi déléguée au BFF Vendeur — c'est-à-dire à
    /// personne.
    ///
    /// **Commandes** : le compteur de ventes est RECALCULÉ depuis order-service à
    /// chaque commande confirmée. Piloter cette réponse est la seule façon de
    /// montrer qu'on POSE la valeur au lieu de l'incrémenter — un compteur
    /// incrémental ne saurait pas redescendre.
    ///
    /// Tout le reste — base, outbox, courtier, consommateur, télémétrie — est réel.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // ═════════════════════════════════════════════════════════════════════
        // LES ERREURS DE L'HÔTE SONT RETENUES, POUR QU'UN 500 PUISSE DIRE
        //    POURQUOI.
        //
        // La réponse ne porte qu'un `correlationId` ; l'exception part dans les
        // journaux, qui ne vivaient que dans la sortie du job. Voir
        // `JournalHote` : c'est ce trou qui a coûté deux jours le 2 septembre.
        //
        // `AddProvider` et non `ClearProviders` : la console reste branchée. Le
        // journal du pas garde donc tout ce qu'il avait, on ne fait qu'AJOUTER
        // un exemplaire des erreurs là où le message d'échec pourra le lire.
        // ═════════════════════════════════════════════════════════════════════
        builder.ConfigureLogging(journalisation =>
            journalisation.AddProvider(new JournalHoteProvider()));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityModuleApi>();
            services.AddScoped<IIdentityModuleApi, IdentiteDeTest>();

            services.RemoveAll<IMediaModuleApi>();
            services.AddSingleton<IMediaModuleApi>(Media);

            services.RemoveAll<IInventoryModuleApi>();
            services.AddSingleton<IInventoryModuleApi>(Inventaire);

            services.RemoveAll<IOrderingModuleApi>();
            services.AddSingleton<IOrderingModuleApi>(Commandes);
        });
    }

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
/// simplement deux fois plus lentement, et personne ne comprendrait pourquoi.
/// </summary>
[CollectionDefinition(Nom)]
public sealed class MerchantsIntegrationCollection : ICollectionFixture<MerchantsIntegrationFixture>
{
    public const string Nom = "merchants-integration";
}
