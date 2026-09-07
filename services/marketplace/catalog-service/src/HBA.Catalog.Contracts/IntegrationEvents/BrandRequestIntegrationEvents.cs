using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

/// <summary>Les deux événements de marque du §19, qui manquaient faute d'agrégat.</summary>
[HbaEvent("catalog", "brand", "requested", Version = 1, AggregateType = "BrandRequest")]
public sealed record BrandRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required Guid SellerId { get; init; }
    public required string Name { get; init; }
}

/// <summary>`BrandId` PEUT DÉSIGNER UNE MARQUE PRÉEXISTANTE.</summary>
[HbaEvent("catalog", "brand", "approved", Version = 1, AggregateType = "BrandRequest")]
public sealed record BrandRequestApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid BrandId { get; init; }
    public required string RequestedName { get; init; }
}
