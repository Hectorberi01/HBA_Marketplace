using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

/// <summary>Publié quand un produit est supprimé.</summary>
[HbaEvent("catalog.product.deleted")]
public sealed record ProductDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
}
