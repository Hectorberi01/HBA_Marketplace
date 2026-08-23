using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HBA.Gateway.IntegrationTests;

/// <summary>
/// Démarre la passerelle en mémoire, sans qu'aucun microservice n'existe.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN SERVICE N'EST SIMULÉ, ET C'EST LE POINT DE CES TESTS.
///
/// Ils vérifient ce que la passerelle décide AVANT de router : authentification,
/// autorisation, limitation de débit, corrélation, forme des erreurs. Toutes ces
/// décisions se prennent sans jamais joindre un service — c'est précisément
/// pourquoi elles sont testables aujourd'hui, alors que les treize services
/// n'existent pas.
///
/// Une requête qui passe tous les contrôles finit en 502 (destination
/// injoignable). Un 502 est donc, ici, la preuve que les contrôles ont été
/// FRANCHIS — l'inverse d'un échec.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public class GatewayFactory : WebApplicationFactory<Program>
{
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

                // Export désactivé : sans cela, chaque test tente d'atteindre le
                // collecteur et attend son délai de connexion.
                ["OpenTelemetry:Endpoint"] = string.Empty,

                // Les destinations doivent être des URL VALIDES — la validation
                // des options échouerait au démarrage — mais n'ont pas à répondre.
                ["Services:Identity"] = "http://127.0.0.1:59001",
                ["Services:Order"] = "http://127.0.0.1:59002",
                ["Services:Catalog"] = "http://127.0.0.1:59003"
            });
        });
    }
}
