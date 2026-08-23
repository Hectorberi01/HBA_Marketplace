using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Offers.Events;

/// <summary>
/// Une offre vient d'être créée. Elle est en BROUILLON : cet événement annonce
/// son existence, pas sa mise en vente.
/// </summary>
public sealed record ProductOfferCreatedDomainEvent(
    Guid OfferId,
    Guid ProductId,
    Guid VariantId,
    Guid StoreId,
    Guid SellerId,
    decimal BuyerPrice,
    string Currency) : DomainEvent;

/// <summary>
/// Le prix acheteur a changé. Porte le prix CALCULÉ, pas le prix vendeur : les
/// consommateurs — recherche, vitrine — affichent ce que paie le client, et leur
/// transmettre le prix net les obligerait à refaire le calcul de commission.
/// </summary>
public sealed record ProductOfferPriceChangedDomainEvent(
    Guid OfferId,
    Guid ProductId,
    decimal BuyerPrice,
    string Currency) : DomainEvent;

/// <summary>
/// Changement d'état. Porte l'état PRÉCÉDENT en plus du nouveau : sans lui, un
/// consommateur ne peut pas distinguer « vient d'être retirée de la vente » de
/// « était déjà retirée », et rejouerait ses effets de bord.
/// </summary>
public sealed record ProductOfferStatusChangedDomainEvent(
    Guid OfferId,
    Guid ProductId,
    string PreviousStatus,
    string NewStatus) : DomainEvent;
