using HBA.Shared.IntegrationEvents;

namespace HBA.Commerce.Contracts.IntegrationEvents;

/// <summary>Le panier a été validé (checkout). Consommé par Ordering / analytics.</summary>
public sealed record CartCheckedOutIntegrationEvent : IntegrationEvent
{
    public required Guid CartId { get; init; }
    public required Guid BuyerId { get; init; }
}
