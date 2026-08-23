using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

public sealed record CategoryCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid CategoryId { get; init; }
    public Guid? ParentId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string Path { get; init; }
}
