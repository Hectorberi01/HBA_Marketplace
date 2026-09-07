using HBA.Shared.Domain.Events;

namespace HBA.Food.Domain.Orders.Events;

/// <summary>LES ÉVÉNEMENTS DE COMMANDE ET DE CUISINE (cahier des charges §19).</summary>
public sealed record FoodOrderReceivedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId, decimal Total, int ItemCount) : DomainEvent;

/// <summary>Acceptée. Vaut aussi <c>kitchen.ticket.created</c>.</summary>
/// <param name="AcceptedByUserId">NUL quand l'acceptation est AUTOMATIQUE (§3).</param>
public sealed record FoodOrderAcceptedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId,
    Guid? AcceptedByUserId, int EstimatedPreparationMinutes) : DomainEvent;

/// <summary>Refusée, avec le motif.</summary>
public sealed record FoodOrderRejectedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId,
    string Reason, string? Comment, Guid RejectedByUserId) : DomainEvent;

/// <summary>La cuisine a commencé. Vaut <c>kitchen.ticket.started</c>.</summary>
public sealed record FoodOrderPreparationStartedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId) : DomainEvent;

/// <summary>L'ÉVÉNEMENT QUI APPELLE UN LIVREUR.</summary>
public sealed record FoodOrderReadyForPickupDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId, DateTime ReadyAtUtc) : DomainEvent;

public sealed record FoodOrderPickedUpDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId) : DomainEvent;

/// <summary>
/// LE SEUL DE CETTE FAMILLE QUI N'EXISTAIT PAS — ET LE RESTAURATEUR N'ÉTAIT JAMAIS
/// PAYÉ.
/// </summary>
public sealed record FoodOrderDeliveredDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId) : DomainEvent;

/// <summary>Annulée.</summary>
public sealed record FoodOrderCancelledDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId, string? Reason, bool WasInKitchen) : DomainEvent;
