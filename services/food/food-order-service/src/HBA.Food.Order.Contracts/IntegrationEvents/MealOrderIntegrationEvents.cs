using HBA.Shared.IntegrationEvents;

namespace HBA.FoodOrders.Contracts.IntegrationEvents;

/// <summary>POURQUOI CES ÉVÉNEMENTS NE S'APPELLENT PAS `OrderPlaced`, `OrderConfirmed`…</summary>
[HbaEvent("food-order.meal.order.placed")]
public sealed record MealOrderPlacedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required Guid CartId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Une ligne figée, telle qu'elle voyage vers la cuisine.</summary>
public sealed record MealOrderLinePayload
{
    public required Guid LineId { get; init; }
    public required Guid MenuItemId { get; init; }
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<MealOrderLineOptionPayload> Options { get; init; } = [];
}

public sealed record MealOrderLineOptionPayload
{
    public required Guid OptionGroupId { get; init; }
    public required Guid OptionId { get; init; }
}

/// <summary>La commande est payée : la cuisine peut la recevoir.</summary>
[HbaEvent("food-order.meal.order.confirmed")]
public sealed record MealOrderConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required decimal ShippingFee { get; init; }
    public required string Currency { get; init; }
    public string? CustomerNote { get; init; }

    /// <summary>Le devis de course figé au paiement.</summary>
    public string? DeliveryQuoteId { get; init; }

    public IReadOnlyList<MealOrderLinePayload> Lines { get; init; } = [];
}

[HbaEvent("food-order.meal.order.cancelled")]
public sealed record MealOrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string Reason { get; init; }
}

[HbaEvent("food-order.meal.order.delivered")]
public sealed record MealOrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
}

/// <summary>La commande est payée mais plus exécutable : elle attend un arbitrage.</summary>
[HbaEvent("food-order.meal.order.under.review")]
public sealed record MealOrderUnderReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>L'arbitrage a conclu à la REPRISE.</summary>
[HbaEvent("food-order.meal.order.resumed.after.review")]
public sealed record MealOrderResumedAfterReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string PreviousReason { get; init; }
}
