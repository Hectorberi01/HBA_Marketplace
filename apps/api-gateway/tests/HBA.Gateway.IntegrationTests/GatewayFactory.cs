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
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUI DOIT PASSER PAR L'ENVIRONNEMENT, ET NON PAR LA CONFIGURATION.
    ///
    /// La passerelle consomme desormais un evenement Kafka — `TokenRevoked`, pour
    /// evincer les verdicts de revocation mis en cache. Elle appelle donc
    /// `AddBuildingBlocksInfrastructure`, qui REFUSE DE DEMARRER quand le
    /// producteur est indisponible alors que le drainage d'outbox est actif.
    ///
    /// Sans les deux lignes ci-dessous, les trente et un tests de ce projet
    /// echouaient d'un bloc a la construction de l'hote — pas sur une assertion,
    /// mais sur un refus de demarrage.
    ///
    /// `OUTBOX_ENABLED` EST LU DANS L'ENVIRONNEMENT, PAS DANS LA CONFIGURATION.
    /// `OutboxRegistration.Enabled` appelle `Environment.GetEnvironmentVariable`
    /// directement : le poser dans `AddInMemoryCollection` n'aurait aucun effet,
    /// et l'echec serait identique et inexplicable.
    ///
    /// LE CONSTRUCTEUR STATIQUE, ET NON `ConfigureWebHost`. Les variables sont
    /// lues par `CreateBuilder` avant que la premiere ligne de `Program` ne
    /// s'execute ; les poser plus tard arriverait apres la lecture.
    ///
    /// CE QUE ÇA NE COUVRE PAS. Aucun test de ce projet n'eprouve le
    /// consommateur lui-meme : avec `Kafka:Enabled=false`, il ne demarre pas. La
    /// coupure immediate d'un jeton revoque n'est donc verifiee par rien.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    static GatewayFactory()
    {
        Environment.SetEnvironmentVariable("OUTBOX_ENABLED", "false");
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

                // Les destinations doivent être des URL VALIDES — la validation
                // des options échouerait au démarrage — mais n'ont pas à répondre.
                ["Services:Identity"] = "http://127.0.0.1:59001",
                ["Services:Order"] = "http://127.0.0.1:59002",
                ["Services:Catalog"] = "http://127.0.0.1:59003"
            });
        });
    }
}
