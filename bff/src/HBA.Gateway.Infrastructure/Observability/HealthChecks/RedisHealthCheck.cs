using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HBA.Gateway.Infrastructure.Observability.HealthChecks;

/// <summary>Le cache repond-il ?</summary>
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
