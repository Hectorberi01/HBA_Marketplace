using HBA.Shared.Domain.Events;

namespace HBA.Commerce.Domain.Carts.Events;

/// <summary>Un panier a été créé pour un acheteur.</summary>
public sealed record CartCreatedDomainEvent(Guid CartId, Guid BuyerId) : DomainEvent;

/// <summary>Un article a été ajouté au panier.</summary>
public sealed record ItemAddedToCartDomainEvent(
    Guid CartId, Guid ItemId, int Quantity, string Kind) : DomainEvent;

/// <summary>Le panier a été validé (checkout) — consommé par Ordering.</summary>
public sealed record CartCheckedOutDomainEvent(Guid CartId, Guid BuyerId) : DomainEvent;
