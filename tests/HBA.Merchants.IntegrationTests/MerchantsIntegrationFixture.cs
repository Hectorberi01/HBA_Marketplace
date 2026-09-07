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
/// SELLER-SERVICE CONTRE SES VRAIES DÉPENDANCES — POSTGRES ET KAFKA, EN CONTENEURS.
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

    /// <summary>
    /// Chaîne de connexion à la base du service, pour vérifier un effet en table.
    /// </summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// LES VARIABLES D'ENVIRONNEMENT SONT POSÉES ICI, PAS DANS UN CONSTRUCTEUR
    /// STATIQUE.
    /// </summary>
    /// <summary>Crée les sujets AVANT que l'hôte ne s'abonne.</summary>
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

        // `seller-service`, LA VALEUR RÉELLE DE `docker-compose.dev.yml`.
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

        // ADRESSE SYNTAXIQUEMENT VALIDE VERS UN PORT FERMÉ.
        Poser("Services__Identity", "http://127.0.0.1:59101");

        // Même raison qu'au-dessus : `AddMediaGrpcClient` et
        // `AddInventoryGrpcClient` lèvent à la construction de l'hôte si l'adresse
        // manque.
        Poser("Services__Media", "http://127.0.0.1:59110");
        Poser("Services__Inventory", "http://127.0.0.1:59104");
        Poser("Services__Order", "http://127.0.0.1:59106");

        Poser("Redis__ConnectionString", string.Empty);

        // L'EXPORT DE TÉLÉMÉTRIE RESTE COUPÉ, L'INSTRUMENTATION NON.
        Poser("OpenTelemetry__Endpoint", string.Empty);

        // SANS CETTE LIGNE, LA DOCUMENTATION N'EST PAS SERVIE ICI.
        Poser("OpenApi__Enabled", "true");
    }

    /// <summary>
    /// Le service média en mémoire, que les tests remplissent avant de rattacher
    /// une pièce KYB. Voir <see cref="MediaDeTest"/> .
    /// </summary>
    internal MediaDeTest Media { get; } = new();

    /// <summary>
    /// L'inventaire en mémoire, que les tests remplissent avant de rattacher un
    /// lieu d'expédition.
    /// </summary>
    internal InventaireDeTest Inventaire { get; } = new();

    /// <summary>order-service en mémoire, pour le compteur de ventes.</summary>
    internal CommandesDeTest Commandes { get; } = new();

    /// <summary>QUATRE VOISINS SUBSTITUÉS, ET AUCUN AUTRE.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // LES ERREURS DE L'HÔTE SONT RETENUES, POUR QU'UN 500 PUISSE DIRE POURQUOI.
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

/// <summary>La collection xUnit qui partage la fixture.</summary>
[CollectionDefinition(Nom)]
public sealed class MerchantsIntegrationCollection : ICollectionFixture<MerchantsIntegrationFixture>
{
    public const string Nom = "merchants-integration";
}
