using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace HBA.Gateway.Infrastructure.ReverseProxy;

/// <summary>
/// Renseigne les destinations des clusters YARP à partir de la section
/// <c>Services</c>, au chargement de la configuration du proxy.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE FILTRE EXISTE.
///
/// Le cahier des charges impose que `Services__Catalog=...` en variable Docker
/// suffise à déplacer un service. Or YARP lit ses adresses dans
/// `ReverseProxy:Clusters:<id>:Destinations:<nom>:Address`. Sans ce filtre, il
/// aurait fallu écrire l'adresse DEUX fois — une pour le proxy, une pour le BFF —
/// et une variable d'environnement n'en aurait déplacé qu'une.
///
/// L'échec aurait été particulièrement pénible à diagnostiquer : le proxy et le
/// BFF auraient tapé sur deux instances DIFFÉRENTES du même service, avec des
/// données divergentes selon le chemin emprunté par la requête.
///
/// `ConfigureClusterAsync` est le point d'extension prévu par YARP pour cela ;
/// il est rejoué à chaque rechargement à chaud de la configuration.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
            //
            // Une exception ici invalide le chargement ENTIER du proxy — un
            // cluster mal nommé mettrait donc les quinze routes hors service,
            // y compris celles qui étaient correctes. On laisse ce cluster sans
            // destination (ses requêtes rendront 503) et on journalise en erreur.
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
