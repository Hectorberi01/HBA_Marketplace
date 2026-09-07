using HBA.Shared.Domain.Events;

namespace HBA.FoodCarts.Domain.Carts.Events;

/// <summary>Un panier de restauration a été ouvert pour un acheteur.</summary>
public sealed record FoodCartCreatedDomainEvent(Guid CartId, Guid BuyerId) : DomainEvent;

/// <summary>Un plat a été ajouté au panier.</summary>
public sealed record FoodItemAddedToCartDomainEvent(
    Guid CartId, Guid RestaurantId, Guid MenuItemId, int Quantity) : DomainEvent;

/// <summary>Le panier a été validé.</summary>
public sealed record FoodCartCheckedOutDomainEvent(
    Guid CartId, Guid BuyerId, Guid RestaurantId) : DomainEvent;
