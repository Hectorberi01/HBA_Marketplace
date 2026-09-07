namespace HBA.Shared.Application.Abstractions;

/// <summary>Le barème de la plateforme, vu par les couches Application.</summary>
public interface IPlatformPricing
{
    /// <summary>Commission plateforme, en fraction du prix vendeur net (0.10 = 10 %).</summary>
    decimal CommissionRate { get; }

    /// <summary>Frais prestataire de paiement, en fraction du prix vendeur net.</summary>
    decimal ProviderFeeRate { get; }

    /// <summary>Commission sur la restauration, en fraction du prix restaurant net.</summary>
    decimal FoodCommissionRate { get; }
}
