using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HBA.Gateway.IntegrationTests;

/// <summary>Démarre la passerelle en mémoire, sans qu'aucun microservice n'existe.</summary>
public class GatewayFactory : WebApplicationFactory<Program>
{
    /// <summary>CE QUI DOIT PASSER PAR L'ENVIRONNEMENT, ET NON PAR LA CONFIGURATION.</summary>
    static GatewayFactory()
    {
        Environment.SetEnvironmentVariable("OUTBOX_ENABLED", "false");

        // L'HOTE DE TEST DECLARAIT « Development » PAR UN CANAL QUE LE SOCLE NE LIT
        // PAS. C'EST CE QUI FAISAIT ECHOUER LES 41 TESTS.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Clé de test explicite : ne pas dépendre de celle
                // d'appsettings.Development.json, qu'un développeur peut changer
                // sans se douter qu'il casse la suite de tests.
                ["Authentication:SigningKey"] = TestTokens.SigningKey,
                ["Authentication:Issuer"] = TestTokens.Issuer,
                ["Authentication:Audience"] = TestTokens.Audience,

                // Kafka éteint : sinon le consommateur d'événements — ajouté avec
                // l'invalidation du cache de révocation — cherche un courtier
                // absent pendant toute la suite.
                ["Kafka:Enabled"] = "false",

                // Export désactivé : sans cela, chaque test tente d'atteindre le
                // collecteur et attend son délai de connexion.
                ["OpenTelemetry:Endpoint"] = string.Empty,

                // Les destinations doivent être des URL VALIDES — la validation des
                // options échouerait au démarrage — mais n'ont pas à répondre.
                ["Services:Identity"] = "http://127.0.0.1:59001",
                ["Services:Order"] = "http://127.0.0.1:59002",
                ["Services:Catalog"] = "http://127.0.0.1:59003"
            });
        });
    }
}
