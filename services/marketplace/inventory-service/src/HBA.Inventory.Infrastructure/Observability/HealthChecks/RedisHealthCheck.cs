using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HBA.Inventory.Infrastructure.Observability.HealthChecks;

/// <summary>
/// Le cache repond-il ?
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ELLE PASSE PAR `IDistributedCache`, PAS PAR UN CLIENT REDIS.
///
/// C'est ce que le service emprunte reellement. Verifier autre chose que le
/// chemin du code, c'est verifier autre chose : une sonde qui ouvrirait sa propre
/// connexion Redis pourrait etre verte pendant que le cache du service, mal
/// configure, retombe en memoire.
///
/// CE QU'ELLE NE DIT PAS : si le cache est PARTAGE. Un service retombe sur le
/// cache memoire repond « Healthy » — il a bien un cache, il n'a simplement pas
/// celui qu'on croit. C'est le message de demarrage d'`AjouterCacheInventory` qui
/// porte cette nuance, pas cette sonde.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class SondeDuCache : IHealthCheck
{
    private const string Cle = "hba:sonde";

    private readonly IDistributedCache _cache;

    public SondeDuCache(IDistributedCache cache) => _cache = cache;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.GetAsync(Cle, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Le cache ne repond pas.", exception);
        }
    }
}
