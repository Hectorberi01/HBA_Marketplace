namespace HBA.Promotions.Contracts;

/// <summary>Ce qu'un panier apporte pour qu'une remise soit calculée.</summary>
/// <param name="Scope">« GLOBAL », « MARKETPLACE » ou « FOOD ».</param>
/// <param name="Subtotal">Sous-total en unités monétaires entières (§2).</param>
/// <param name="DeliveryFee">
/// Frais de livraison, séparés parce qu'une remise peut ne toucher qu'eux.
/// </param>
public sealed record PromotionEvaluationContext(
    string Scope, long Subtotal, long DeliveryFee, string Currency, Guid UserId);

/// <summary>Verdict d'une évaluation.</summary>
/// <param name="SellerFundedDiscount">
/// <summary> Part de <c> Discount</c> supportée par le VENDEUR
/// propriétaire.</summary>
/// </param>
/// <param name="PlatformFundedDiscount">
/// <summary> Part de <c> Discount</c> supportée par la PLATEFORME.</summary>
/// </param>
/// <param name="OwnerSellerId">
/// Le vendeur qui finance. <c> null</c> = campagne de la plateforme.
/// </param>
public sealed record PromotionEvaluationResult(
    bool Valid,
    Guid? PromotionId,
    long Discount,
    string Currency,
    string Message,
    string? Reason,
    long SellerFundedDiscount = 0,
    long PlatformFundedDiscount = 0,
    Guid? OwnerSellerId = null);

/// <summary>Une retenue accordée, à engager au paiement ou à laisser expirer.</summary>
public sealed record CouponReservationResult(
    Guid ReservationId, Guid CouponId, Guid PromotionId,
    long DiscountAmount, string Currency, DateTime ExpiresAtUtc);

/// <summary>L'API DE PROMOTION POUR LES AUTRES SERVICES (§10.16, contrats gRPC).</summary>
public interface IPromotionModuleApi
{
    /// <summary>Calcule la remise SANS rien consommer.</summary>
    Task<PromotionEvaluationResult> EvaluateAsync(
        string code, PromotionEvaluationContext context, CancellationToken cancellationToken = default);

    /// <summary>Retient le coupon pour ce panier et consomme le budget.</summary>
    Task<CouponReservationResult?> ReserveAsync(
        string code, Guid userId, Guid cartId, PromotionEvaluationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Engage la retenue : la commande est payée, l'usage devient définitif.</summary>
    Task<bool> CommitAsync(
        Guid reservationId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Libère une retenue non engagée et rend le budget immédiatement.</summary>
    Task<bool> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default);
}
