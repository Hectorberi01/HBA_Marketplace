using HBA.DeliveryPricing.Contracts;
using System.Net.Http.Headers;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using HBA.Commerce.Contracts;
using HBA.Deliveries.Contracts;
using HBA.Inventory.Contracts;
using HBA.Products.Contracts;
using HBA.Tests.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ORDER-SERVICE CONTRE SES VRAIES DÉPENDANCES — POSTGRES ET KAFKA, EN CONTENEURS.
///
/// CE QUE CETTE SUITE EXISTE POUR PROUVER : QUE LES DEUX CHAÎNES QUI TIENNENT
///    L'ARGENT ARRIVENT VRAIMENT JUSQU'ICI.
///
/// ISSUE-002 et ISSUE-003 décrivent la même panne, dans les deux sens :
///
///   • `PaymentCapturedIntegrationEvent` n'atteignait pas order-service.
///     L'acheteur est débité, la commande reste `AwaitingPayment`
///     indéfiniment : ARGENT ENCAISSÉ SANS CONTREPARTIE ;
///   • `PaymentFailedIntegrationEvent` non plus. La commande échoue, le stock
///     reste réservé sans limite de temps : SURVENTE PAR ÉTRANGLEMENT — c'est
///     cumulatif, chaque paiement échoué en retire un peu plus.
///
/// Les deux gestionnaires EXISTAIENT et étaient bien enregistrés
/// (`OrderingModuleInstaller`, lignes 74-75). Ils ne recevaient rien parce que
/// payment-service publiait sur `service.payment.v1` pendant qu'order-service
/// écoutait `service.financial.v1`. Le câblage avait donc toutes les apparences
/// d'être correct : seul l'EFFET manquait. Aucun test unitaire ne pouvait le
/// voir — entre le gestionnaire et le message publié il y a une sérialisation,
/// un nom de sujet, un consommateur et un dispatcher, quatre endroits où le lien
/// se rompt sans casser la compilation.
///
/// ET C'EST LA PREMIÈRE FOIS QUE LES DIX-SEPT MIGRATIONS SONT REJOUÉES À FROID.
///
/// `check-migrations.py` les LIT, il ne les exécute pas. `AjoutInboxConsommateur`
/// — la table sans laquelle le rejeu du §19.5 ne serait pas gardé — date de cette
/// session et n'avait jamais rencontré un vrai PostgreSQL.
///
/// POURQUOI UNE FIXTURE DE COLLECTION ET NON UNE PAR CLASSE.
///
/// Démarrer Postgres et Kafka coûte quelques dizaines de secondes. Par classe, la
/// suite deviendrait assez lente pour qu'on cesse de la lancer — et une suite
/// qu'on ne lance pas ne vaut rien. Les tests doivent donc rester INDÉPENDANTS de
/// l'ordre : chacun crée son acheteur, son panier et sa commande.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class OrderIntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hba_orders")
        .WithUsername("hba")
        .WithPassword("hba")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.6.1")
        .Build();

    /// <summary>Adresse du courtier, pour qu'un test produise ou lise lui-même.</summary>
    public string BootstrapServers => _kafka.GetBootstrapAddress();

    /// <summary>Chaîne de connexion à la base du service, pour constater un effet en table.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// Crée les sujets AVANT que l'hôte ne s'abonne.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SANS CELA, LE CONSOMMATEUR EST ABONNÉ À DES SUJETS QUI N'EXISTENT PAS.
    ///
    /// Un sujet Kafka naît à la première publication. Le test qui injecte le
    /// paiement crée donc `service.financial.v1` APRÈS que l'hôte s'y est abonné.
    /// librdkafka ne redemande la liste des sujets qu'à intervalle régulier —
    /// `TopicMetadataRefreshIntervalMs` vaut vingt secondes ici, cinq MINUTES par
    /// défaut : entre-temps, le service est abonné à un sujet qu'il ne voit pas,
    /// ne consomme rien, et ne journalise rien d'anormal.
    ///
    /// Le symptôme serait une attente de quatre-vingt-dix secondes qui expire sur
    /// une commande restée `AwaitingPayment` — c'est-à-dire l'image EXACTE de la
    /// panne qu'on cherche à démontrer close. On chercherait un défaut de sujet
    /// là où il n'y en a plus, et c'est ce qui a fait échouer une suite entière.
    ///
    /// C'EST AUSSI CE QUE FAIT LA PRODUCTION. Les sujets y sont provisionnés
    /// par `k8s/overlays/*/kafka-topics.yaml`, avec leurs partitions et leur
    /// rétention ; aucun service ne les crée à la volée — le consommateur pose
    /// d'ailleurs `AllowAutoCreateTopics = false`. Les provisionner ici rapproche
    /// le test du réel au lieu de l'en éloigner.
    ///
    /// Une partition et un facteur de réplication de 1 : un courtier unique en
    /// conteneur, et l'ordre par clé est de toute façon garanti dans une partition.
    /// </remarks>
    private async Task ProvisionnerLesSujetsAsync()
    {
        string[] sujets =
        [
            // Là où le test INJECTE le paiement, et là où order-service PUBLIE
            // ce qu'il en fait. Les deux noms sortent de `HbaTopics` — voir
            // l'encadré de `BusDeTest`.
            BusDeTest.SujetFinancial,
            BusDeTest.SujetOrder
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
    /// Et ce doit être des VARIABLES D'ENVIRONNEMENT : les treize services
    /// utilisent l'hébergement minimal — `WebApplication.CreateBuilder(args)` —
    /// et lisent leur configuration DÈS LA DEUXIÈME LIGNE de `Program`, quand les
    /// rappels de `ConfigureAppConfiguration` ne s'exécutent qu'au `Build()`
    /// final. Une valeur fournie par là serait parfaitement fournie… et
    /// parfaitement inutile : `AddHbaService` lèverait « Chaîne de connexion
    /// « Default » absente » avant de l'avoir vue.
    ///
    /// Séparateur `__` et non `:` — convention des variables d'environnement.
    /// Une clé écrite `ConnectionStrings:Default` serait ignorée EN SILENCE.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        await ProvisionnerLesSujetsAsync();

        static void Poser(string cle, string valeur)
            => Environment.SetEnvironmentVariable(cle, valeur);

        Poser("ASPNETCORE_ENVIRONMENT", "Testing");

        // ═════════════════════════════════════════════════════════════════════
        // `order-service`, LA VALEUR RÉELLE DE `docker-compose.dev.yml`.
        //
        // Elle décide de DEUX choses à la fois : le sujet sur lequel ce service
        // publie — `HbaTopics.Pour(options, "order-service")` → `service.order.v1`
        // — et son groupe de consommateurs. La poser à autre chose ferait lire au
        // test un sujet que la production n'emploie pas ; un harnais qui s'écarte
        // de la production ne prouve que lui-même.
        //
        // Le domaine d'order-service coïncide avec son nom de conteneur, donc la
        // traduction est ici l'identité. Ce n'est PAS le cas du producteur qu'on
        // imite : `payment-service` → domaine `financial`. Voir `BusDeTest`.
        // ═════════════════════════════════════════════════════════════════════
        Poser("SERVICE_NAME", "order-service");

        Poser("ConnectionStrings__Default", _postgres.GetConnectionString());

        // VRAI, CONTRAIREMENT AUX TESTS D'AUTORISATION. C'est tout l'objet de ce
        // niveau : le schéma est construit par les migrations réelles, sur une base
        // vide, à chaque exécution.
        Poser("Database__MigrateOnStartup", "true");

        Poser("Kafka__Enabled", "true");
        Poser("Kafka__BootstrapServers", _kafka.GetBootstrapAddress());

        // SANS L'OUTBOX, LA MOITIÉ DU PARCOURS N'EST PAS ÉPROUVÉE.
        //
        // La confirmation de la commande RAISE un événement de domaine, qui
        // devient un événement d'intégration, qui part en table, que le
        // processeur publie. Le test qui compte les `order.confirmed` sur le
        // courtier ne prouve rien si personne ne draine cette table.
        Poser("OUTBOX_ENABLED", "true");

        Poser("Authentication__SigningKey", TestTokens.SigningKey);
        Poser("Authentication__Issuer", TestTokens.Issuer);
        Poser("Authentication__Audience", TestTokens.Audience);

        // `AddHbaGrpc` la réclame pour l'interception de service à service. Sa
        // valeur n'a aucune importance, sa présence si.
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

        // ═════════════════════════════════════════════════════════════════════
        // SEPT ADRESSES SYNTAXIQUEMENT VALIDES VERS DES PORTS FERMÉS.
        //
        // `AddXxxGrpcClient` lève à la CONSTRUCTION de l'hôte quand l'adresse
        // manque — pas au premier appel. `HBA.Order.Api/Program.cs` en appelle
        // sept : Inventory, Commerce, Delivery, DeliveryPricing, Merchant, Food,
        // Catalog. Quatre
        // sont substituées plus bas ; Merchant et Food ne sont touchées par
        // aucun chemin de cette suite (`ListBySellerAsync` pour merchant, la
        // traduction d'une référence `FOOD-` pour food), et un port fermé est
        // alors la réponse la plus honnête : si un futur chemin de code les
        // appelait, le test le dirait au lieu de l'absorber.
        //
        // CETTE LISTE EST TENUE À LA MAIN, ET ELLE A DÉJÀ PRIS UN LOT DE
        // RETARD. Le lot 7.4 a branché `AddProductsGrpcClient` dans le
        // `Program.cs` sans toucher ici : trois tests sont tombés au démarrage
        // sur « Services:Catalog est absent ». C'était la cinquième occurrence
        // du même motif dans ce dépôt. `check-service-addresses.py` compare
        // désormais chaque fabrique de test au `Program.cs` qu'elle démarre.
        // ═════════════════════════════════════════════════════════════════════
        Poser("Services__Inventory", "http://127.0.0.1:59104");
        Poser("Services__Commerce", "http://127.0.0.1:59105");
        Poser("Services__Delivery", "http://127.0.0.1:59108");

        // LE DEVIS SE RELIT CHEZ delivery-pricing DEPUIS QUE `LookupQuote` EST
        // BRANCHÉ — `DeliveryApi.LookupQuote` n'avait jamais eu de corps.
        //
        // Port fermé, et c'est correct : les tests de cette suite passent commande
        // SANS devis (`DeliveryQuoteId` nul), et `DeliveryQuoteLookupClient` ne
        // touche pas le réseau dans ce cas. Le jour où un test posera un devis,
        // il faudra un double — et l'échec de connexion le dira franchement,
        // plutôt que de rendre un montant inventé.
        Poser("Services__DeliveryPricing", "http://127.0.0.1:59110");
        Poser("Services__Merchant", "http://127.0.0.1:59107");
        Poser("Services__Food", "http://127.0.0.1:59109");
        Poser("Services__Catalog", "http://127.0.0.1:59103");

        // VIDE, ET NON UNE ADRESSE VERS UN PORT FERMÉ : le socle retomberait
        // sinon sur une CONNEXION à chaque lecture de cache, avec le délai
        // d'attente par défaut de StackExchange.Redis à chaque requête.
        Poser("Redis__ConnectionString", string.Empty);

        // L'EXPORT DE TÉLÉMÉTRIE RESTE COUPÉ, L'INSTRUMENTATION NON.
        //
        // Aucun collecteur ne tourne ici : une adresse ferait réessayer
        // l'exportateur en tâche de fond et noierait la sortie des tests sous des
        // erreurs de connexion sans rapport.
        Poser("OpenTelemetry__Endpoint", string.Empty);
    }

    /// <summary>
    /// Le panier valorisé en mémoire, que les tests remplissent avant de passer
    /// commande. Voir <see cref="PanierDeTest"/>.
    /// </summary>
    /// <remarks>
    /// `internal` ET NON `public` : la fixture est publique parce que xUnit
    /// l'exige, mais les faux restent internes — ils n'ont rien à faire dans la
    /// surface publique de l'assembly. Les classes de test vivent dans le même
    /// assembly et y accèdent sans difficulté.
    /// </remarks>
    internal PanierDeTest Panier { get; } = new();

    /// <summary>
    /// L'inventaire en mémoire, qui ENREGISTRE ce qu'on lui demande. C'est lui
    /// qui porte la preuve d'ISSUE-003. Voir <see cref="InventaireDeTest"/>.
    /// </summary>
    internal InventaireDeTest Inventaire { get; } = new();

    /// <summary>
    /// delivery-service en mémoire. Voir <see cref="CourseDeTest"/>.
    /// </summary>
    internal CourseDeTest Courses { get; } = new();

    /// <summary>
    /// La relecture de devis, qui LÈVE. Voir <see cref="DevisDeTest"/>.
    /// </summary>
    internal DevisDeTest Devis { get; } = new();

    /// <summary>
    /// Le catalogue interrogé par la revalidation du prix au checkout (ISSUE-048).
    /// Nominalement complaisant ; les trois refus se demandent explicitement.
    /// </summary>
    internal CatalogueDeTest Catalogue { get; } = new();

    /// <summary>
    /// TROIS VOISINS SUBSTITUÉS, ET AUCUN AUTRE.
    ///
    /// **Inventaire** — c'est LUI la preuve d'ISSUE-003. Le gestionnaire d'échec
    /// de paiement n'écrit rien qu'on puisse lire dans `ordering` au sujet du
    /// stock : la libération est un APPEL SORTANT vers inventory-service, qui
    /// n'existe pas dans ce test. Un double qui enregistre ses appels est donc le
    /// seul observable possible — et le seul moyen de vérifier « une libération
    /// PAR LIGNE » plutôt que « au moins une libération ».
    ///
    /// **Panier** — `PlaceOrderCommandHandler` lit le panier valorisé pour figer
    /// ses prix ; sans lui, aucune commande n'existe et il n'y a rien à
    /// confirmer. Faire tourner cart-service en conteneur ferait de chaque test
    /// de cette suite un test de DEUX services : une migration cassée chez le
    /// voisin ferait échouer le parcours de paiement, et l'on chercherait ici une
    /// panne qui n'y est pas.
    ///
    /// **Courses** — order-service consomme SA PROPRE confirmation
    /// (`CreateDeliveryOnOrderConfirmedHandler`, enregistré dans `Program.cs`) et
    /// demande alors une course. Sans double, cet appel gRPC frapperait un port
    /// fermé, LÈVERAIT, et le consommateur le rejouerait trois fois avant de
    /// l'abandonner en Critical — six secondes de bruit sans rapport avec ce
    /// qu'on éprouve. Le double rend la suite lisible, et permet en prime de
    /// constater que la course EST demandée : c'est le maillon sans lequel aucun
    /// vendeur n'est jamais réglé.
    ///
    /// Tout le reste — base, migrations, outbox, courtier, consommateur, inbox,
    /// dispatcher, télémétrie — est réel. En particulier, RIEN de ce qui porte
    /// ISSUE-002 et ISSUE-003 n'est substitué : ni le nom du sujet, ni le
    /// consommateur, ni les deux gestionnaires, ni la garde d'idempotence.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICartModuleApi>();
            services.AddSingleton<ICartModuleApi>(Panier);

            services.RemoveAll<IInventoryModuleApi>();
            services.AddSingleton<IInventoryModuleApi>(Inventaire);

            // ═════════════════════════════════════════════════════════════
            // LES DEUX INTERFACES SONT RETIRÉES, UNE SEULE EST REMPLACÉE.
            //
            // `AddDeliveryGrpcClient` en enregistre deux sur la même
            // implémentation : `IDeliveryDispatchApi` (écriture) et
            // `IDeliveryModuleApi` (lecture). order-service n'injecte JAMAIS la
            // seconde — aucune occurrence dans tout le service.
            //
            // La laisser en place ferait subsister, sous une interface que
            // personne n'utilise, un client gRPC pointant vers un port fermé :
            // le jour où quelqu'un l'injecterait, le test partirait en délai
            // d'attente réseau au lieu de dire ce qui manque. Retirée, le
            // conteneur refuse de construire l'hôte avec un message qui NOMME le
            // service non résolu. Un échec bruyant vaut mieux qu'un échec lent.
            // ═════════════════════════════════════════════════════════════
            services.RemoveAll<IDeliveryDispatchApi>();
            services.RemoveAll<IDeliveryModuleApi>();
            services.AddSingleton<IDeliveryDispatchApi>(Courses);

            // LA RELECTURE DE DEVIS EST UN TROISIÈME ENREGISTREMENT, ET IL VIENT
            // D'UN AUTRE CLIENT.
            //
            // `AddDeliveryPricingGrpcClient` apporte `IDeliveryQuoteLookup` —
            // `DeliveryApi.LookupQuote` n'ayant jamais eu de corps de serveur, le
            // devis se relit chez delivery-pricing. Il est résolu à la
            // CONSTRUCTION de `PlaceOrderCommandHandler` : sans substitution, tout
            // test qui passe commande construirait un client vers un port fermé.
            // `DevisDeTest` lève au lieu de répondre, et dit pourquoi.
            services.RemoveAll<IDeliveryQuoteLookup>();
            services.AddSingleton<IDeliveryQuoteLookup>(Devis);

            // SUBSTITUÉ, PAS SEULEMENT ADRESSÉ. Depuis le lot 7.4, le catalogue
            // n'est pas seulement exigé au démarrage : il est APPELÉ à chaque
            // commande, pour revalider prix et achetabilité. Se contenter de
            // l'adresse vers un port fermé aurait remplacé trois erreurs de
            // construction par une erreur à l'appel dans chaque test qui commande —
            // plus lente, et pointant vers le réseau au lieu du manque.
            services.RemoveAll<IProductsModuleApi>();
            services.AddSingleton<IProductsModuleApi>(Catalogue);
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
public sealed class OrderIntegrationCollection : ICollectionFixture<OrderIntegrationFixture>
{
    public const string Nom = "order-integration";
}
