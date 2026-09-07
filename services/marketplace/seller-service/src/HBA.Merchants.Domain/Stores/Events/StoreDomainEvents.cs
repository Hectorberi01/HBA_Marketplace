using HBA.Shared.Domain.Events;

namespace HBA.Merchants.Domain.Stores.Events;

/// <summary>Une boutique vient d'être créée (état Draft : rien n'est encore en vente).</summary>
public sealed record StoreCreatedDomainEvent(Guid StoreId, Guid SellerId, string Name) : DomainEvent;

/// <summary>Une boutique ouvre : ses offres redeviennent achetables.</summary>
public sealed record StoreOpenedDomainEvent(Guid StoreId, Guid SellerId) : DomainEvent;

/// <summary>Une boutique ferme — que ce soit par décision du vendeur ou de la plateforme.</summary>
public sealed record StoreClosedDomainEvent(Guid StoreId, Guid SellerId, string? Reason) : DomainEvent;

/// <summary>LA BOUTIQUE EST SUSPENDUE PAR LA PLATEFORME.</summary>
public sealed record StoreSuspendedDomainEvent(
    Guid StoreId, Guid SellerId, string? Reason) : DomainEvent;

/// <summary>La plateforme lève la sanction.</summary>
public sealed record StoreSuspensionLiftedDomainEvent(Guid StoreId, Guid SellerId) : DomainEvent;
