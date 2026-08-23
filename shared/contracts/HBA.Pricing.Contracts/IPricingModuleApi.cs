namespace HBA.Pricing.Contracts;

/// <summary>
/// API in-process publique du module Pricing. Cart et Ordering l'appellent pour
/// obtenir le prix effectif d'une ligne (avec la trace des financeurs), sans
/// jamais accéder à la base des promotions.
/// </summary>
public interface IPricingModuleApi
{
    Task<PriceBreakdownDto> CalculatePriceAsync(PriceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide un code promo pour un acheteur, avant de l'attacher au panier.
    /// Verdict indicatif — voir <see cref="CouponValidation"/>.
    /// </summary>
    /// <param name="cartSubtotal">
    /// ═════════════════════════════════════════════════════════════════════════
    /// AJOUTÉ PAR LE LOT D28, ET CE N'EST PAS UN CONFORT.
    ///
    /// Une campagne peut porter une condition « panier d'au moins 5 000 F »
    /// (`MINIMUM_SUBTOTAL`). Sans le sous-total, cette validation ne peut pas
    /// l'évaluer : elle répond « code valide », l'acheteur l'attache, et découvre
    /// au calcul du panier qu'aucune remise ne s'applique — c'est-à-dire
    /// exactement le parcours que cette méthode existe pour éviter.
    ///
    /// `0` vaut « l'appelant ne sait pas » : le fournisseur répond alors sur la
    /// seule question qu'il puisse trancher — ce code désigne-t-il une campagne
    /// VIVANTE ? — et la condition de montant reste à découvrir plus tard.
    /// Le défaut garde compilables les appelants écrits avant D28 (règle D32).
    /// ═════════════════════════════════════════════════════════════════════════
    /// </param>
    Task<CouponValidation> ValidateCouponAsync(
        string code, Guid buyerId, decimal cartSubtotal = 0m, CancellationToken cancellationToken = default);
}
