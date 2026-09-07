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
        // LES SONDES SONT ANONYMES, ET C'EST NÉCESSAIRE.

        // Vivacité : le processus répond.
        app.MapHealthChecks("/health/live", new()
        {
            Predicate = _ => false
        }).AllowAnonymous();

        // Aptitude : la passerelle peut prendre du trafic.
        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains(ReadyTag)
        }).AllowAnonymous();

        // Conservée : `compose.services.yml` et `compose.gateway.yml` sondent déjà
        // `/health`.
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
public sealed class ProxyConfigurationHealthCheck : IHealthCheck
{
    private readonly IProxyStateLookup _proxy;

    // PAS DE `IOptionsMonitor<ServicesOptions>` ICI.
    public ProxyConfigurationHealthCheck(IProxyStateLookup proxy) => _proxy = proxy;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // `GetClusters()` rend un `IEnumerable` : on le matérialise une fois, sans
        // quoi les deux parcours ci-dessous réénumèreraient l'état du proxy.
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
            // Dégradé et non « en panne » : les autres routes fonctionnent.
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Clusters sans destination : {string.Join(", ", withoutDestination)}. "
                + "Vérifier la section Services."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{clusters.Length} cluster(s) routable(s)."));
    }
}
