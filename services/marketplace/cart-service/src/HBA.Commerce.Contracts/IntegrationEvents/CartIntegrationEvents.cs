using HBA.Shared.IntegrationEvents;

namespace HBA.Commerce.Contracts.IntegrationEvents;

/// <summary>Le panier a été validé (checkout).</summary>
[HbaEvent("commerce.cart.checked.out")]
public sealed record CartCheckedOutIntegrationEvent : IntegrationEvent
{
    public required Guid CartId { get; init; }
    public required Guid BuyerId { get; init; }
}
