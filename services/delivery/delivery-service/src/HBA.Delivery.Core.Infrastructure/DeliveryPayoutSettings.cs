using HBA.Deliveries.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace HBA.Deliveries.Infrastructure.Configuration;

/// <summary>LA PART DU LIVREUR, LUE DANS LA CONFIGURATION.</summary>
public sealed class DeliveryPayoutSettings : IDeliveryPayoutSettings
{
    public const string SectionKey = "Delivery:DriverSharePercent";

    /// <summary>Part retenue à défaut de réglage.</summary>
    public const decimal DefaultSharePercent = 70m;

    public DeliveryPayoutSettings(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration[SectionKey];

        if (string.IsNullOrWhiteSpace(raw))
        {
            DriverShareRate = DefaultSharePercent / 100m;
            UsesDefault = true;
            return;
        }

        // Culture INVARIANTE, explicitement.
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var percent))
        {
            throw new InvalidOperationException(
                $"« {SectionKey} » vaut « {raw} », qui n'est pas un nombre. Attendu : un pourcentage, "
                + "par exemple « 70 » pour 70 %.");
        }

        if (percent is < 0m or > 100m)
        {
            throw new InvalidOperationException(
                $"« {SectionKey} » vaut {percent}. Un pourcentage de partage se situe entre 0 et 100 — "
                + "au-delà, le livreur toucherait plus que le prix de la course.");
        }

        DriverShareRate = percent / 100m;
        UsesDefault = false;
    }

    public decimal DriverShareRate { get; }

    /// <summary>Vrai si aucun réglage n'a été fourni.</summary>
    public bool UsesDefault { get; }
}
