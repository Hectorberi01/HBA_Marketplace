using HBA.Shared.Domain.Events;

namespace HBA.FoodOrders.Domain.Orders.Events;

/// <summary>La commande est enregistrée et attend son paiement.</summary>
public sealed record MealOrderPlacedDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, Guid CartId, decimal GrandTotal, string Currency) : DomainEvent;

/// <summary>Le paiement est encaissé : la cuisine peut recevoir le ticket.</summary>
public sealed record MealOrderConfirmedDomainEvent(
    Guid OrderId,
    Guid BuyerId,
    Guid RestaurantId,
    decimal GrandTotal,
    decimal ShippingFee,
    string Currency,
    string? PromotionCode,
    string? DeliveryQuoteId,
    string? CustomerNote,
    IReadOnlyCollection<MealOrderConfirmedLine> Lines) : DomainEvent;

/// <summary>Une ligne telle qu'elle voyage vers la cuisine.</summary>
public sealed record MealOrderConfirmedLine(
    Guid LineId,
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    IReadOnlyCollection<(Guid GroupId, Guid OptionId)> Options);

/// <summary>La commande a été annulée.</summary>
public sealed record MealOrderCancelledDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, string Reason) : DomainEvent;

/// <summary>Le repas a été remis au client : escrow à libérer, restaurateur à régler.</summary>
public sealed record MealOrderDeliveredDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId) : DomainEvent;

/// <summary>La commande est payée mais plus exécutable : elle attend un arbitrage.</summary>
public sealed record MealOrderUnderReviewDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, string Reason) : DomainEvent;

/// <summary>L'arbitrage a conclu à la REPRISE.</summary>
public sealed record MealOrderResumedAfterReviewDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, string PreviousReason) : DomainEvent;
