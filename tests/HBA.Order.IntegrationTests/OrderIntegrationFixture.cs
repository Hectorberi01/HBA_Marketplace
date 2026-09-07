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
/// ORDER-SERVICE CONTRE SES VRAIES DÉPENDANCES — POSTGRES ET KAFKA, EN CONTENEURS.
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

    /// <summary>
    /// Chaîne de connexion à la base du service, pour constater un effet en table.
    /// </summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Crée les sujets AVANT que l'hôte ne s'abonne.</summary>
    private async Task ProvisionnerLesSujetsAsync()
    {
        string[] sujets =
        [
            // Là où le test INJECTE le paiement, et là où order-service PUBLIE ce
            // qu'il en fait.
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
    /// STATIQUE.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        await ProvisionnerLesSujetsAsync();

        static void Poser(string cle, string valeur)
            => Environment.SetEnvironmentVariable(cle, valeur);

        Poser("ASPNETCORE_ENVIRONMENT", "Testing");

        // `order-service`, LA VALEUR RÉELLE DE `docker-compose.dev.yml`.
        Poser("SERVICE_NAME", "order-service");

        Poser("ConnectionStrings__Default", _postgres.GetConnectionString());

        // VRAI, CONTRAIREMENT AUX TESTS D'AUTORISATION. C'est tout l'objet de ce
        // niveau : le schéma est construit par les migrations réelles, sur une base
        // vide, à chaque exécution.
        Poser("Database__MigrateOnStartup", "true");

        Poser("Kafka__Enabled", "true");
        Poser("Kafka__BootstrapServers", _kafka.GetBootstrapAddress());

        // SANS L'OUTBOX, LA MOITIÉ DU PARCOURS N'EST PAS ÉPROUVÉE.
        Poser("OUTBOX_ENABLED", "true");

        Poser("Authentication__SigningKey", TestTokens.SigningKey);
        Poser("Authentication__Issuer", TestTokens.Issuer);
        Poser("Authentication__Audience", TestTokens.Audience);

        // `AddHbaGrpc` la réclame pour l'interception de service à service.
        Poser("Internal__ApiKey", "cle-interne-de-test");

        // UNE VRAIE PAIRE DE CLÉS, ET NON LE MODE NON SIGNÉ.
        var nomDeLHote = typeof(Program).Assembly.GetName().Name!;

        using (var identite = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Poser("Internal__ServiceName", nomDeLHote);
            Poser("Internal__PrivateKey",
                Convert.ToBase64String(identite.ExportPkcs8PrivateKey()));
            Poser("Internal__PublicKeys",
                $"{nomDeLHote}={Convert.ToBase64String(identite.ExportSubjectPublicKeyInfo())}");
        }

        // SEPT ADRESSES SYNTAXIQUEMENT VALIDES VERS DES PORTS FERMÉS.
        Poser("Services__Inventory", "http://127.0.0.1:59104");
        Poser("Services__Commerce", "http://127.0.0.1:59105");
        Poser("Services__Delivery", "http://127.0.0.1:59108");

        // LE DEVIS SE RELIT CHEZ delivery-pricing DEPUIS QUE `LookupQuote` EST
        // BRANCHÉ — `DeliveryApi.LookupQuote` n'avait jamais eu de corps.
        Poser("Services__DeliveryPricing", "http://127.0.0.1:59110");
        Poser("Services__Merchant", "http://127.0.0.1:59107");
        Poser("Services__Food", "http://127.0.0.1:59109");
        Poser("Services__Catalog", "http://127.0.0.1:59103");

        // VIDE, ET NON UNE ADRESSE VERS UN PORT FERMÉ : le socle retomberait sinon
        // sur une CONNEXION à chaque lecture de cache, avec le délai d'attente par
        // défaut de StackExchange.Redis à chaque requête.
        Poser("Redis__ConnectionString", string.Empty);

        // L'EXPORT DE TÉLÉMÉTRIE RESTE COUPÉ, L'INSTRUMENTATION NON.
        Poser("OpenTelemetry__Endpoint", string.Empty);
    }

    /// <summary>
    /// Le panier valorisé en mémoire, que les tests remplissent avant de passer
    /// commande.
    /// </summary>
    internal PanierDeTest Panier { get; } = new();

    /// <summary>L'inventaire en mémoire, qui ENREGISTRE ce qu'on lui demande.</summary>
    internal InventaireDeTest Inventaire { get; } = new();

    /// <summary>delivery-service en mémoire.</summary>
    internal CourseDeTest Courses { get; } = new();

    /// <summary>La relecture de devis, qui LÈVE. Voir <see cref="DevisDeTest"/>.</summary>
    internal DevisDeTest Devis { get; } = new();

    /// <summary>
    /// Le catalogue interrogé par la revalidation du prix au checkout (ISSUE-048).
    /// </summary>
    internal CatalogueDeTest Catalogue { get; } = new();

    /// <summary>TROIS VOISINS SUBSTITUÉS, ET AUCUN AUTRE.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICartModuleApi>();
            services.AddSingleton<ICartModuleApi>(Panier);

            services.RemoveAll<IInventoryModuleApi>();
            services.AddSingleton<IInventoryModuleApi>(Inventaire);

            // LES DEUX INTERFACES SONT RETIRÉES, UNE SEULE EST REMPLACÉE.
            services.RemoveAll<IDeliveryDispatchApi>();
            services.RemoveAll<IDeliveryModuleApi>();
            services.AddSingleton<IDeliveryDispatchApi>(Courses);

            // LA RELECTURE DE DEVIS EST UN TROISIÈME ENREGISTREMENT, ET IL VIENT
            // D'UN AUTRE CLIENT.
            services.RemoveAll<IDeliveryQuoteLookup>();
            services.AddSingleton<IDeliveryQuoteLookup>(Devis);

            // SUBSTITUÉ, PAS SEULEMENT ADRESSÉ.
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

/// <summary>La collection xUnit qui partage la fixture.</summary>
[CollectionDefinition(Nom)]
public sealed class OrderIntegrationCollection : ICollectionFixture<OrderIntegrationFixture>
{
    public const string Nom = "order-integration";
}
