using HBA.Pricing.Contracts;

namespace HBA.FoodCarts.Infrastructure.Public;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TARIFICATION NEUTRE DU PANIER DE REPAS — AUCUNE REMISE, AUCUN CODE ACCEPTÉ.
///
/// CE FICHIER EXISTAIT EN DEUX EXEMPLAIRES. IL N'EN RESTE QUE CELUI-CI.
///
/// Le jumeau de cart-service a été remplacé par `PromotionPricingModuleApi`, qui
/// appelle réellement promotion-service (ISSUE-033, décision D28). Celui-ci reste
/// parce que brancher le panier de repas demande davantage que la même ligne
/// d'enregistrement : `FoodCartPricer` ne transporte ni acheteur ni sous-total de
/// panier vers `PriceRequest`, l'univers d'évaluation serait `FOOD` et non
/// `MARKETPLACE`, et personne n'a encore décidé quelle campagne l'emporte quand un
/// panier de repas relève des deux périmètres. Ce n'est pas beaucoup de travail,
/// c'est du travail QUI N'A PAS ÉTÉ FAIT, et l'écrire à l'aveugle dans ce lot
/// aurait produit un second chemin de calcul de remise que personne n'a éprouvé.
///
/// IL REFUSE LES CODES AU LIEU DE LES ACCEPTER, ET C'EST L'ORDRE JUSTE.
///
/// Un bouchon permissif accorderait une remise que personne n'a décidée et que la
/// comptabilité devrait ensuite retrouver. Un bouchon strict fait échouer une
/// saisie de code — un désagrément VISIBLE, qui se corrige en branchant promotion.
///
/// ET POURTANT IL REFUSE DE DÉMARRER EN PRODUCTION. VOICI POURQUOI.
///
/// Le §11 décrit un checkout de restauration avec code promo ; l'application
/// affiche le champ ; `PromotionScope.Food` existe dans le domaine de promotion et
/// n'a AUCUN appelant. Un exploitant qui déploie ce service croit donc livrer une
/// fonctionnalité qui ne peut pas marcher, et rien dans les journaux ne le lui
/// dit : un panier sans remise ressemble exactement à un panier sans coupon.
///
/// C'est la règle que ce dépôt s'est donnée aux vagues 0.3, 3.2 et 3.4 —
/// `SimulatedPayoutGateway`, les passerelles sans remboursement, les adaptateurs
/// gRPC bouchonnés de return-refund : un adaptateur qui n'a pas de contrepartie
/// réelle refuse de démarrer en production, et s'annonce bruyamment ailleurs. Voir
/// `FoodCartModuleInstaller.GuardNeutralPricing`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class NeutralPricingModuleApi : IPricingModuleApi
{
    public Task<PriceBreakdownDto> CalculatePriceAsync(
        PriceRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new PriceBreakdownDto(
            BaseAmount: request.BaseAmount,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            FinalAmount: request.BaseAmount,
            Currency: request.Currency));

    /// <summary>
    /// `cartSubtotal` EST REÇU ET IGNORÉ, ET C'EST HONNÊTE.
    ///
    /// Le paramètre a été ajouté au contrat par D28 pour qu'une condition « panier
    /// d'au moins 5 000 F » puisse être évaluée AVANT que l'acheteur n'attache son
    /// code. Ici il n'y a rien à évaluer : tout code est refusé. Le déclarer et
    /// l'ignorer vaut mieux que de faire porter au contrat une signature différente
    /// selon l'implémentation.
    /// </summary>
    public Task<CouponValidation> ValidateCouponAsync(
        string code, Guid buyerId, decimal cartSubtotal = 0m,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CouponValidation.Invalid(
            "pricing.unavailable",
            "Les codes promo ne sont pas encore disponibles sur les commandes de repas."));
}
