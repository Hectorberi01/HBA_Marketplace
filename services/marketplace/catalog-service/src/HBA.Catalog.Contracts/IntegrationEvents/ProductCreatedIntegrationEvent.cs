using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

/// <summary>Publié sur le bus quand un produit est créé.</summary>
[HbaEvent("product.created")]
public sealed record ProductCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid CategoryId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
}
