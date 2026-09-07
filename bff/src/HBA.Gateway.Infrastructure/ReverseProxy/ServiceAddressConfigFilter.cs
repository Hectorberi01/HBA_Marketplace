using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace HBA.Gateway.Infrastructure.ReverseProxy;

/// <summary>
/// Renseigne les destinations des clusters YARP à partir de la section <c>
/// Services</c>, au chargement de la configuration du proxy.
/// </summary>
public sealed class ServiceAddressConfigFilter : IProxyConfigFilter
{
    private const string DestinationName = "primary";

    private readonly IOptionsMonitor<ServicesOptions> _services;
    private readonly ILogger<ServiceAddressConfigFilter> _logger;

    public ServiceAddressConfigFilter(
        IOptionsMonitor<ServicesOptions> services,
        ILogger<ServiceAddressConfigFilter> logger)
    {
        _services = services;
        _logger = logger;
    }

    public ValueTask<RouteConfig> ConfigureRouteAsync(
        RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
        => ValueTask.FromResult(route);

    public ValueTask<ClusterConfig> ConfigureClusterAsync(
        ClusterConfig cluster, CancellationToken cancel)
    {
        var address = _services.CurrentValue.Resolve(cluster.ClusterId);

        if (string.IsNullOrWhiteSpace(address))
        {
            // ON NE LÈVE PAS : YARP ABANDONNERAIT TOUTE LA CONFIGURATION.
            _logger.LogError(
                "Cluster YARP {ClusterId} : aucune adresse dans la section Services. "
                + "Les requêtes vers ce cluster échoueront. Clés connues : {Known}",
                cluster.ClusterId, string.Join(", ", ServiceKeys.All));

            return ValueTask.FromResult(cluster);
        }

        return ValueTask.FromResult(cluster with
        {
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                [DestinationName] = new DestinationConfig { Address = address }
            }
        });
    }
}
