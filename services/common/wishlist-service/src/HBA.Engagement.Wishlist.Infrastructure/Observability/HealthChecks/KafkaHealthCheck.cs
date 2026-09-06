using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HBA.Engagement.Wishlist.Infrastructure.Observability.HealthChecks;

/// <summary>
/// Le courtier Kafka repond-il ?
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE TROU QUE CETTE SONDE FERME.
///
/// Jusqu'ici, `ready` ne verifiait QUE la base. Un service dont le consommateur
/// Kafka etait mort repondait donc « pret », et le deploiement individuel
/// (`up -d --no-deps`) le croyait sain. C'est exactement la panne de ce mois-ci,
/// a l'endroit ou l'on venait de gagner en visibilite.
///
/// ELLE INTERROGE VRAIMENT LE COURTIER. Une sonde qui rendrait « Healthy » sans
/// rien verifier serait pire que pas de sonde : elle donnerait une garantie
/// fausse — le defaut meme que ce depot refuse partout ailleurs.
///
/// L'`IAdminClient` EST CONSTRUIT UNE FOIS. La sonde est appelee toutes les
/// quelques secondes par l'orchestrateur ; ouvrir une connexion a chaque appel
/// couterait plus que ce qu'elle mesure.
///
/// CE QU'ELLE NE DIT PAS : que CE service consomme. Elle dit que le courtier est
/// joignable et que les metadonnees arrivent. Un groupe de consommateurs bloque
/// en rebalancement perpetuel la laisserait verte — pour cela il faudrait lire le
/// decalage du groupe, ce que cette sonde ne fait pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class SondeDeKafka : IHealthCheck, IDisposable
{
    private static readonly TimeSpan Delai = TimeSpan.FromSeconds(3);

    private readonly IAdminClient? _admin;

    public SondeDeKafka(IConfiguration configuration)
    {
        var serveurs = configuration["Kafka:BootstrapServers"];

        // KAFKA ETEINT N'EST PAS UNE PANNE. Les harnais de tests posent
        // « Kafka:Enabled=false » ; une sonde rouge y ferait echouer des tests qui
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
