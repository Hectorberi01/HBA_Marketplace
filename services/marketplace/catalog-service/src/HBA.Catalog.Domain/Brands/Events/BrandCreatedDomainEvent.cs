using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Brands.Events;

public sealed record BrandCreatedDomainEvent(Guid BrandId, string Name, string Slug) : DomainEvent;
