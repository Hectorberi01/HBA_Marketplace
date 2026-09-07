using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HBA.Merchants.Infrastructure.Observability.HealthChecks;

/// <summary>Les destinations gRPC de ce service sont-elles configurees ?</summary>
internal sealed class SondeDesDestinationsGrpc : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public SondeDesDestinationsGrpc(IConfiguration configuration) => _configuration = configuration;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var absentes = _configuration.GetSection("Services").GetChildren()
            .Where(entree => string.IsNullOrWhiteSpace(entree.Value)
                             || !Uri.TryCreate(entree.Value, UriKind.Absolute, out _))
            .Select(entree => entree.Key)
            .ToArray();

        return Task.FromResult(absentes.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded(
                "Destinations gRPC absentes ou illisibles : " + string.Join(", ", absentes)));
    }
}
