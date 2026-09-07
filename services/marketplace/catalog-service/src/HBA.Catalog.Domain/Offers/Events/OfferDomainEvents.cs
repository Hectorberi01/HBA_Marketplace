using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Offers.Events;

/// <summary>Une offre vient d'être créée.</summary>
public sealed record ProductOfferCreatedDomainEvent(
    Guid OfferId,
    Guid ProductId,
    Guid VariantId,
    Guid StoreId,
    Guid SellerId,
    decimal BuyerPrice,
    string Currency) : DomainEvent;

/// <summary>Le prix acheteur a changé.</summary>
public sealed record ProductOfferPriceChangedDomainEvent(
    Guid OfferId,
    Guid ProductId,
    decimal BuyerPrice,
    string Currency) : DomainEvent;

/// <summary>
/// Changement d'état. Porte l'état PRÉCÉDENT en plus du nouveau : sans lui, un
/// consommateur ne peut pas distinguer « vient d'être retirée de la vente » de «
/// était déjà retirée », et rejouerait ses effets de bord.
/// </summary>
public sealed record ProductOfferStatusChangedDomainEvent(
    Guid OfferId,
    Guid ProductId,
    string PreviousStatus,
    string NewStatus) : DomainEvent;
