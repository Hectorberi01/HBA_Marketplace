using HBA.Shared.IntegrationEvents;

namespace HBA.FoodCarts.Contracts.IntegrationEvents;

/// <summary>Le panier de restauration a été clos parce que la commande est partie.</summary>
[HbaEvent("food-cart.food.cart.checked.out")]
public sealed record FoodCartCheckedOutIntegrationEvent : IntegrationEvent
{
    public required Guid CartId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
}
