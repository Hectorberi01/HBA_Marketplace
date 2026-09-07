using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Products.Events;

/// <summary>UNE IMAGE A ÉTÉ DÉTACHÉE D'UN PRODUIT — SON FICHIER RESTE À EFFACER.</summary>
public sealed record ProductMediaRemovedDomainEvent(
    Guid ProductId,
    Guid MediaId) : DomainEvent;
