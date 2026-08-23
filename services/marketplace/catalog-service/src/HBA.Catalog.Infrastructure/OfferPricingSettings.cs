using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Infrastructure.Configuration;

namespace HBA.Catalog.Infrastructure;

/// <summary>Le barème des offres, lu depuis la source unique de la plateforme.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// IL NE LIT PAS LA CONFIGURATION LUI-MÊME, ET C'EST LE POINT.
///
/// La tentation était d'écrire ici `configuration["Catalog:CommissionRate"]`.
/// Ce serait recréer exactement le défaut que la tâche S9 a corrigé : la
/// commission avait alors DEUX sources — une pour les offres, une pour le
/// règlement des vendeurs — dans deux unités différentes. Changer le taux
/// demandait d'éditer deux clés ; en oublier une facturait l'acheteur à un taux
/// et payait le vendeur à un autre, sans qu'aucune erreur ne se déclenche.
///
/// `PlatformPricing` est cette source unique. Elle lit `Pricing:*`, refuse de
/// démarrer sur une valeur aberrante, et rejette explicitement les anciennes
/// clés (`Products:CommissionPercent`…) plutôt que de basculer en silence sur un
/// défaut.
///
/// CETTE CLASSE N'EST DONC PAS UN RÉGLAGE, C'EST UN ADAPTATEUR. Elle traduit
/// un contrat de socle en un contrat d'application, pour que le domaine des
/// offres ne dépende pas de `HBA.Shared.Infrastructure`.
///
/// CE QU'ELLE NE FAIT TOUJOURS PAS : le taux NÉGOCIÉ d'un vendeur
/// (`SellerSummary.CommissionRate`) et les règles par catégorie restent hors du
/// calcul. Voir l'encadré de `IOfferPricingSettings` et les tâches #192 / #193.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class OfferPricingSettings : IOfferPricingSettings
{
    public OfferPricingSettings(PlatformPricing pricing)
    {
        CommissionRate = pricing.CommissionRate;
        ProviderFeeRate = pricing.ProviderFeeRate;
    }

    public decimal CommissionRate { get; }

    public decimal ProviderFeeRate { get; }
}
