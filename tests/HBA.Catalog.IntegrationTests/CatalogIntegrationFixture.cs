using System.Net.Http.Headers;
using HBA.Tests.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Security.Cryptography;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace HBA.Catalog.IntegrationTests;

/// <summary>CATALOG CONTRE SES VRAIES DÉPENDANCES — POSTGRES ET KAFKA, EN CONTENEURS.</summary>
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

    /// <summary>
    /// Chaîne de connexion à la base du service, pour vérifier un effet en table.
    /// </summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// LES VARIABLES D'ENVIRONNEMENT SONT POSÉES ICI, PAS DANS UN CONSTRUCTEUR
    /// STATIQUE.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        static void Poser(string cle, string valeur)
            => Environment.SetEnvironmentVariable(cle, valeur);

        Poser("ASPNETCORE_ENVIRONMENT", "Testing");
        Poser("SERVICE_NAME", "catalog-service");

        Poser("ConnectionStrings__Default", _postgres.GetConnectionString());

        // VRAI, CONTRAIREMENT AUX TESTS D'AUTORISATION. C'est tout l'objet de ce
        // niveau : le schéma est construit par les migrations réelles, sur une base
        // vide, à chaque exécution.
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

        // ADRESSES SYNTAXIQUEMENT VALIDES VERS DES PORTS FERMÉS.
        Poser("Services__Merchant", "http://127.0.0.1:59107");
        Poser("Services__Media", "http://127.0.0.1:59110");
        Poser("Services__Inventory", "http://127.0.0.1:59104");
        Poser("Services__Identity", "http://127.0.0.1:59101");

        Poser("Redis__ConnectionString", string.Empty);

        // L'EXPORT DE TÉLÉMÉTRIE RESTE COUPÉ, L'INSTRUMENTATION NON.
        Poser("OpenTelemetry__Endpoint", string.Empty);

        // SANS CETTE LIGNE, `La_documentation_openapi_est_servie_sans_jeton` ÉCHOUE
        // — ET IL N'AVAIT JAMAIS ÉTÉ EXÉCUTÉ POUR LE DIRE.
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

/// <summary>La collection xUnit qui partage la fixture.</summary>
[CollectionDefinition(Nom)]
public sealed class CatalogIntegrationCollection : ICollectionFixture<CatalogIntegrationFixture>
{
    public const string Nom = "catalog-integration";
}
