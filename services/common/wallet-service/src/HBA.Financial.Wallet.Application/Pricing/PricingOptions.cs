namespace HBA.Financial.Wallet.Application.Pricing;

/// <summary>
/// Options de tarification du module Settlement (section de configuration « Pricing
/// »).
/// </summary>
public sealed class PricingOptions
{
    /// <summary>LE BARÈME D'AFFICHAGE — CE N'EST PLUS LE TAUX RÉELLEMENT PRÉLEVÉ.</summary>
    public decimal PlatformCommissionRate { get; set; } = 0.10m;

    /// <summary>Taux des frais provider, en fraction du prix vendeur net (0.05 = 5 %).</summary>
    public decimal ProviderFeeRate { get; set; } = 0.05m;

    /// <summary>COMMISSION SUR LA RESTAURATION, DISTINCTE DE CELLE DE LA MARCHANDISE.</summary>
    public decimal FoodCommissionRate { get; set; } = 0.10m;
}
