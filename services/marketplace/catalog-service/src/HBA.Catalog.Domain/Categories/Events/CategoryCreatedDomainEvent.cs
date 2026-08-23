using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Categories.Events;

public sealed record CategoryCreatedDomainEvent(
    Guid CategoryId,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path) : DomainEvent;
