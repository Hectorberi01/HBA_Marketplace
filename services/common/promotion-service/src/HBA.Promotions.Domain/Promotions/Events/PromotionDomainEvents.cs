using HBA.Shared.Domain.Events;

namespace HBA.Promotions.Domain.Promotions.Events;

/// <summary>Une campagne vient d'être créée (§10.16, <c>promotion.created</c>).</summary>
public sealed record PromotionCreatedDomainEvent(
    Guid PromotionId, string Name, string Scope, string Type, long Value,
    DateTime StartsAtUtc, DateTime EndsAtUtc, long? Budget, string Currency,
    int SellerFundedShareBps = 0, Guid? OwnerSellerId = null) : DomainEvent;

/// <summary>Le budget est consommé (§10.16, <c>promotion.exhausted</c>).</summary>
public sealed record PromotionExhaustedDomainEvent(
    Guid PromotionId, string Name, long BudgetConsumed) : DomainEvent;

/// <summary>
/// Un coupon vient d'être engagé sur une commande payée (§10.16, <c>
/// coupon.used</c>).
/// </summary>
public sealed record CouponUsedDomainEvent(
    Guid CouponId, Guid PromotionId, string Code, Guid UserId, Guid OrderId, long DiscountAmount) : DomainEvent;
