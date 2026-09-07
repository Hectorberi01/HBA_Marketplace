namespace HBA.Catalog.Application.Abstractions;

/// <summary>Barème appliqué au prix vendeur pour obtenir le prix acheteur.</summary>
public interface IOfferPricingSettings
{
    /// <summary>Part plateforme, entre 0 et 1.</summary>
    decimal CommissionRate { get; }

    /// <summary>Frais du prestataire de paiement, entre 0 et 1.</summary>
    decimal ProviderFeeRate { get; }
}
