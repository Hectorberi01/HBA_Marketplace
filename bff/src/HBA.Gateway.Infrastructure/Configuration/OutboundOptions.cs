using System.ComponentModel.DataAnnotations;

namespace HBA.Gateway.Infrastructure.Configuration;

/// <summary>Délais et résilience des appels sortants du BFF.</summary>
public sealed class OutboundOptions
{
    public const string SectionName = "HttpClients";

    /// <summary>
    /// Délai TOTAL accordé à un appel sortant, réessais compris.
    /// </summary>
    /// <remarks>
    /// Valeur initiale à ajuster sur des mesures réelles, pas une règle métier.
    /// Elle doit rester inférieure au budget d'agrégation <c>Bff:Timeout</c>,
    /// sinon le budget coupe avant le timeout et le disjoncteur ne voit jamais
    /// d'échec : il ne s'ouvrira pas, et chaque requête repaiera l'attente
    /// complète vers un service pourtant durablement à terre.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.500", "00:01:00")]
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Délai d'une tentative isolée.</summary>
    [Range(typeof(TimeSpan), "00:00:00.200", "00:01:00")]
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Nombre de RÉESSAIS après l'échec initial (0 = aucun).</summary>
    [Range(0, 5)]
    public int MaxRetryAttempts { get; init; } = 2;

    /// <summary>Proportion d'échecs à partir de laquelle le disjoncteur s'ouvre.</summary>
    [Range(0.1, 1.0)]
    public double CircuitBreakerFailureRatio { get; init; } = 0.5;

    /// <summary>Durée d'ouverture du disjoncteur avant nouvelle tentative.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan CircuitBreakerDuration { get; init; } = TimeSpan.FromSeconds(15);
}
