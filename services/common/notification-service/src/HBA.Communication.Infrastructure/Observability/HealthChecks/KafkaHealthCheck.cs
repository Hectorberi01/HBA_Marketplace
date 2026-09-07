using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HBA.Communication.Infrastructure.Observability.HealthChecks;

/// <summary>Le courtier Kafka repond-il ?</summary>
internal sealed class SondeDeKafka : IHealthCheck, IDisposable
{
    private static readonly TimeSpan Delai = TimeSpan.FromSeconds(3);

    private readonly IAdminClient? _admin;

    public SondeDeKafka(IConfiguration configuration)
    {
        var serveurs = configuration["Kafka:BootstrapServers"];

        // KAFKA ETEINT N'EST PAS UNE PANNE. Les harnais de tests posent «
        // Kafka:Enabled=false » ; une sonde rouge y ferait echouer des tests qui
        // n'ont rien a voir avec le bus.
        var actif = !string.Equals(configuration["Kafka:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

        if (actif && !string.IsNullOrWhiteSpace(serveurs))
        {
            _admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = serveurs }).Build();
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_admin is null)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Kafka desactive pour cet hote."));
        }

        try
        {
            var metadonnees = _admin.GetMetadata(Delai);

            return Task.FromResult(metadonnees.Brokers.Count > 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Aucun courtier dans les metadonnees."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Le courtier ne repond pas.", exception));
        }
    }

    public void Dispose() => _admin?.Dispose();
}
