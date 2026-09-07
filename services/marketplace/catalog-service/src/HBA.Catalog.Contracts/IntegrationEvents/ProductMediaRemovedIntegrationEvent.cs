using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

/// <summary>
/// Publié quand une image cesse d'être rattachée à un produit — détachée une par
/// une, ou emportée par la suppression du produit.
/// </summary>
[HbaEvent("catalog.product.media.removed")]
public sealed record ProductMediaRemovedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }

    public required Guid MediaId { get; init; }
}
