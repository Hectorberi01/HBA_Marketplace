using HBA.Shared.IntegrationEvents;

namespace HBA.Promotions.Contracts.IntegrationEvents;

/// <summary>Une campagne vient d'être créée (§10.16, <c>promotion.created</c>).</summary>
[HbaEvent("promotion.created", Version = 1, AggregateType = "Promotion")]
public sealed record PromotionCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid PromotionId { get; init; }

    public required string Name { get; init; }

    /// <summary>« GLOBAL », « MARKETPLACE » ou « FOOD ».</summary>
    public required string Scope { get; init; }

    /// <summary>« PERCENT », « FIXED » ou « FREE_DELIVERY ».</summary>
    public required string Type { get; init; }

    /// <summary>15 pour 15 %, ou un montant en unités entières (§2).</summary>
    public required long Value { get; init; }

    public required DateTime StartsAtUtc { get; init; }

    public required DateTime EndsAtUtc { get; init; }

    /// <summary>Null = pas de plafond global.</summary>
    public long? Budget { get; init; }

    public required string Currency { get; init; }

    /// <summary>Part de la remise supportée par le VENDEUR, en points de base (D28).</summary>
    public int? SellerFundedShareBps { get; init; }

    /// <summary>Vendeur propriétaire de la campagne.</summary>
    public Guid? OwnerSellerId { get; init; }
}

/// <summary>Le budget d'une campagne est consommé (§10.16, <c>promotion.exhausted</c>).</summary>
[HbaEvent("promotion.exhausted", Version = 1, AggregateType = "Promotion")]
public sealed record PromotionExhaustedIntegrationEvent : IntegrationEvent
{
    public required Guid PromotionId { get; init; }

    public required string Name { get; init; }

    public required long BudgetConsumed { get; init; }
}

/// <summary>Un coupon a été engagé sur une commande payée (§10.16, <c>coupon.used</c>).</summary>
[HbaEvent("coupon.used", Version = 1, AggregateType = "Coupon")]
public sealed record CouponUsedIntegrationEvent : IntegrationEvent
{
    public required Guid CouponId { get; init; }

    public required Guid PromotionId { get; init; }

    /// <summary>Le code saisi, normalisé en majuscules.</summary>
    public required string Code { get; init; }

    public required Guid UserId { get; init; }

    public required Guid OrderId { get; init; }

    /// <summary>Montant réellement accordé, en unités monétaires entières (§2).</summary>
    public required long DiscountAmount { get; init; }
}
