namespace HBA.Catalog.Application.Abstractions;

/// <summary>
/// Barème appliqué au prix vendeur pour obtenir le prix acheteur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DOMAINE REÇOIT LES TAUX, IL NE LES LIT PAS.
///
/// `ProductOffer.ComputeBuyerPrice` prend un `OfferPricingRates` en paramètre.
/// C'est ce qui permet à un test de fixer un barème sans monter un conteneur, et
/// ce qui empêche l'agrégat de dépendre d'un fichier de réglages.
///
/// CETTE INTERFACE EST UN TAUX GLOBAL, ET C'EST UNE DETTE CONNUE.
///
/// Elle rend UN couple de taux, identique pour tous. Or la plateforme en a déjà
/// deux autres sources, et elles ne disent pas la même chose :
///
///   • `SellerSummary.CommissionRate` (merchant-service) — le taux NÉGOCIÉ avec
///     ce vendeur-là. C'est celui que l'application vendeur affiche.
///   • les règles de commission par CATÉGORIE — écrites, et inertes : aucun
///     calcul ne les consulte (tâche #192).
///
/// Tant que cette interface sert seule, une offre est tarifée au taux global
/// pendant que le vendeur lit son taux négocié à l'écran. Les deux chiffres
/// divergeront, et c'est le vendeur qui s'en apercevra sur son relevé.
///
/// NE PAS « CORRIGER » EN LISANT merchant-service DEPUIS LE DOMAINE. Le
/// raccordement se fait dans le HANDLER, qui a déjà `ISellerModuleApi` à sa
/// disposition côté catalog-service : il compose les taux et les passe. Voir les
/// tâches #192 et #193, volontairement laissées hors de la phase 3 — les
/// mélanger reviendrait à discuter du barème pendant qu'on met les offres en
/// place.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IOfferPricingSettings
{
    /// <summary>Part plateforme, entre 0 et 1.</summary>
    decimal CommissionRate { get; }

    /// <summary>Frais du prestataire de paiement, entre 0 et 1.</summary>
    decimal ProviderFeeRate { get; }
}
