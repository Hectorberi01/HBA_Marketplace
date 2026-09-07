using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Products.Events;

/// <summary>Fait de domaine : un produit vient d'être créé.</summary>
public sealed record ProductCreatedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Slug) : DomainEvent;
