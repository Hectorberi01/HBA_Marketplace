using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HBA.FoodCarts.Infrastructure.Observability.HealthChecks;

/// <summary>
/// Les destinations gRPC de ce service sont-elles configurees ?
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ELLE N'EST PAS DANS `ready`, ET C'EST LE POINT LE PLUS IMPORTANT DE CE FICHIER.
///
/// Une sonde de disponibilite qui echoue parce qu'un AUTRE service est tombe
/// transforme une panne en N pannes : l'orchestrateur retire du service un
/// conteneur parfaitement sain, dont la seule faute est d'avoir un voisin malade.
/// C'est le mecanisme par lequel une panne d'un service devient une panne de
/// plateforme.
///
/// Elle est donc taguee `dependencies`, rend `Degraded` et JAMAIS `Unhealthy`, et
/// ne sert qu'a un tableau de bord — pas a une decision de redemarrage.
///
/// CE QU'ELLE VERIFIE REELLEMENT : que les adresses declarees sont presentes et
/// analysables. Pas qu'elles repondent — le disjoncteur et l'echeance de cinq
/// secondes s'en chargent au moment de l'appel, la ou l'information sert.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
