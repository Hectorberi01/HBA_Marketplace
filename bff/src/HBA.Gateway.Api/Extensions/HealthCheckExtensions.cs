using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy;
namespace HBA.Gateway.Api.Extensions;

public static class HealthCheckExtensions
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddGatewayHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck<ProxyConfigurationHealthCheck>(
                "proxy-configuration", tags: [ReadyTag]);

        return services;
    }

    public static WebApplication MapGatewayHealthChecks(this WebApplication app)
    {
        // ═════════════════════════════════════════════════════════════════════
        // LES SONDES SONT ANONYMES, ET C'EST NÉCESSAIRE.
        //
        // La politique de repli exige un utilisateur authentifié. Sans
        // `AllowAnonymous`, Docker et Kubernetes recevraient 401 : le conteneur
        // serait déclaré malsain, redémarré en boucle, et le journal ne montrerait
        // qu'un 401 sans dire QUI le reçoit.
        //
        // Elles n'exposent aucune donnée : ni nom d'hôte interne, ni détail de
        // panne — seulement « Healthy » ou « Unhealthy ».
        // ═════════════════════════════════════════════════════════════════════

        // Vivacité : le processus répond. Aucun contrôle de dépendance.
        app.MapHealthChecks("/health/live", new()
        {
            Predicate = _ => false
        }).AllowAnonymous();

        // Aptitude : la passerelle peut prendre du trafic.
        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains(ReadyTag)
        }).AllowAnonymous();

        // Conservée : `compose.services.yml` et `compose.gateway.yml` sondent
        // déjà `/health`. La retirer casserait les healthchecks existants.
        app.MapHealthChecks("/health", new()
        {
            Predicate = _ => false
        }).AllowAnonymous();

        return app;
    }
}

/// <summary>
/// Vérifie que la passerelle est CAPABLE de router : configuration d'adresses
/// valide et clusters chargés.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE SONDE NE CONTACTE AUCUN MICROSERVICE. C'EST DÉLIBÉRÉ.
///
/// Faire dépendre l'aptitude de la passerelle de la santé des services amont
/// paraît rigoureux et produit exactement l'inverse : le redémarrage d'un service
/// non critique — les avis, les notifications — rendrait la passerelle « not
/// ready », l'orchestrateur la sortirait de la rotation, et TOUTE la plateforme
/// deviendrait injoignable à cause d'un composant secondaire.
///
/// Une passerelle dont catalog-service est à terre reste parfaitement apte : elle
/// rend 502 sur `/api/catalog` et sert normalement les quatorze autres routes.
/// C'est le comportement voulu, et c'est le sens de la mise en garde du §12.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ProxyConfigurationHealthCheck : IHealthCheck
{
    private readonly IProxyStateLookup _proxy;

    // PAS DE `IOptionsMonitor<ServicesOptions>` ICI.
    //
    // Le réflexe est d'injecter les options « pour vérifier qu'elles sont
    // valides ». Ce serait du code mort : `ValidateOnStart` a déjà refusé le
    // démarrage si elles ne l'étaient pas, et la seule chose observable ensuite
    // est leur EFFET — un cluster avec ou sans destination, ce que l'on lit ici.
    public ProxyConfigurationHealthCheck(IProxyStateLookup proxy) => _proxy = proxy;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // `GetClusters()` rend un `IEnumerable` : on le matérialise une fois,
        // sans quoi les deux parcours ci-dessous réénumèreraient l'état du proxy.
        var clusters = _proxy.GetClusters().ToArray();

        if (clusters.Length == 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Aucun cluster YARP chargé : la section ReverseProxy est absente ou invalide."));
        }

        var withoutDestination = clusters
            .Where(cluster => cluster.Model.Config.Destinations is null
                              || cluster.Model.Config.Destinations.Count == 0)
            .Select(cluster => cluster.ClusterId)
            .ToArray();

        if (withoutDestination.Length > 0)
        {
            // Dégradé et non « en panne » : les autres routes fonctionnent. Sortir
            // la passerelle de la rotation pour un cluster mal nommé coûterait plus
            // cher que les 503 qu'il produit.
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Clusters sans destination : {string.Join(", ", withoutDestination)}. "
                + "Vérifier la section Services."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{clusters.Length} cluster(s) routable(s)."));
    }
}
