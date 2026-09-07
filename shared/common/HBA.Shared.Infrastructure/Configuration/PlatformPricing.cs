using HBA.Shared.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace HBA.Shared.Infrastructure.Configuration;

/// <summary>LE BARÈME DE LA PLATEFORME — UNE SEULE DÉFINITION, POUR TOUT LE MONDE.</summary>
public sealed class PlatformPricing : IPlatformPricing
{
    public const string CommissionKey = "Pricing:PlatformCommissionRate";
    public const string ProviderFeeKey = "Pricing:ProviderFeeRate";

    /// <summary>Commission sur la restauration.</summary>
    public const string FoodCommissionKey = "Pricing:FoodCommissionRate";

    /// <summary>Clés ABANDONNÉES. Leur présence fait échouer le démarrage.</summary>
    private static readonly (string Key, string Replacement)[] LegacyKeys =
    [
        ("Products:CommissionPercent", CommissionKey),
        ("Products:ProviderFeePercent", ProviderFeeKey),
        ("Billing:DefaultCommissionRate", CommissionKey)
    ];

    private const decimal DefaultCommissionRate = 0.10m;
    private const decimal DefaultProviderFeeRate = 0.05m;

    // MÊME DÉFAUT QUE LA MARCHANDISE, ET CE N'EST PAS UNE RECOMMANDATION.
    private const decimal DefaultFoodCommissionRate = 0.10m;

    public PlatformPricing(IConfiguration configuration)
    {
        RejectLegacyKeys(configuration);

        CommissionRate = Read(configuration, CommissionKey, DefaultCommissionRate);
        ProviderFeeRate = Read(configuration, ProviderFeeKey, DefaultProviderFeeRate);
        FoodCommissionRate = Read(configuration, FoodCommissionKey, DefaultFoodCommissionRate);
    }

    /// <summary>Commission plateforme, en fraction du prix vendeur net (0.10 = 10 %).</summary>
    public decimal CommissionRate { get; }

    /// <summary>Frais prestataire de paiement, en fraction du prix vendeur net.</summary>
    public decimal ProviderFeeRate { get; }

    /// <summary>Commission sur la restauration, en fraction du prix restaurant net.</summary>
    public decimal FoodCommissionRate { get; }

    private static void RejectLegacyKeys(IConfiguration configuration)
    {
        foreach (var (key, replacement) in LegacyKeys)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                throw new InvalidOperationException(
                    $"« {key} » n'est plus lu : le barème de la plateforme a une source unique. "
                    + $"Reportez sa valeur dans « {replacement} » — attention, en TAUX (0.12 pour 12 %), "
                    + "et non en pourcentage — puis retirez l'ancienne clé.");
            }
        }
    }

    private static decimal Read(IConfiguration configuration, string key, decimal fallback)
    {
        var raw = configuration[key];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        // Culture INVARIANTE, explicitement : s'appuyer sur un réglage global pour
        // lire un taux de commission serait fragile.
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate))
        {
            throw new InvalidOperationException($"« {key} » vaut « {raw} », qui n'est pas un nombre. Attendu : un taux, par exemple « 0.1 » pour 10 %.");
        }
        
        
        // ON REFUSE DE DÉMARRER SUR UNE VALEUR ABERRANTE.
        if (rate is < 0m or > 0.5m)
        {
            throw new InvalidOperationException(
                $"« {key} » vaut {rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}. "
                + "Attendu : un TAUX entre 0 et 0.5 (0.1 pour 10 %). "
                + "Une valeur comme « 10 » est un pourcentage — elle multiplierait la commission par cent, "
                + "sur chaque article du catalogue.");
        }

        return rate;
    }
}
