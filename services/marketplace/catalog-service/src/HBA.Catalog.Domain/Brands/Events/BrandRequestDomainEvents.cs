using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Brands.Events;

/// <summary>
/// Un vendeur demande une marque absente du référentiel (§19 : <c>
/// catalog.brand.requested</c>).
/// </summary>
public sealed record BrandRequestedDomainEvent(
    Guid RequestId,
    Guid SellerId,
    string Name) : DomainEvent;

/// <summary>La demande est approuvée (§19 : <c>catalog.brand.approved</c>).</summary>
public sealed record BrandRequestApprovedDomainEvent(
    Guid RequestId,
    Guid SellerId,
    Guid BrandId,
    string RequestedName) : DomainEvent;
