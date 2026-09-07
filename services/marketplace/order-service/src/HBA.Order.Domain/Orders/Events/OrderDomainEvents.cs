using HBA.Shared.Domain.Events;

namespace HBA.Orders.Domain.Orders.Events;

/// <summary>Part d'UN vendeur dans une commande : ce qu'il a vendu, et pour combien.</summary>
/// <param name="SellerId">Le vendeur concerné.</param>
/// <param name="ItemCount">Nombre d'articles de CE vendeur (somme des quantités).</param>
/// <param name="Amount">Montant dû à CE vendeur pour cette commande.</param>
public sealed record OrderSellerShare(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>Une commande a été placée (stock réservé, en attente de paiement).</summary>
public sealed record OrderPlacedDomainEvent(Guid OrderId, Guid BuyerId, Guid CartId, decimal GrandTotal, string Currency) : DomainEvent;

/// <summary>La commande a été confirmée (paiement encaissé, stock soldé).</summary>
/// <param name="Kind">
/// « Goods » ou « Food ». Sans lui, les sept consommateurs de la confirmation
/// traitent un repas comme un colis — Shipping en tête, qui créerait une expédition
/// attribuée au vendeur « 00000000-… ».
/// </param>
/// <param name="RestaurantId">L'établissement qui prépare, ou null hors restauration.</param>
/// <param name="OrderId">La commande confirmée.</param>
/// <param name="BuyerId">L'acheteur.</param>
/// <param name="Currency">La devise des montants de la répartition.</param>
/// <param name="PromotionCode">Le code promo consommé par cette vente, ou null.</param>
/// <param name="SellerShares">Les vendeurs concernés et la part de chacun.</param>
public sealed record OrderConfirmedDomainEvent(
    Guid OrderId,
    Guid BuyerId,
    string Currency,
    string? PromotionCode,
    IReadOnlyCollection<OrderSellerShare> SellerShares,
    string Kind,
    Guid? RestaurantId) : DomainEvent;

/// <summary>La commande a été annulée (réservations libérées).</summary>
/// <param name="OrderId">La commande annulée.</param>
/// <param name="BuyerId">L'acheteur, pour la notification.</param>
/// <param name="Reason">
/// Le motif, en texte LIBRE. C'est le seul champ qui distingue les trois
/// annulations ci-dessus, et il n'offre aucune prise fiable pour les compter
/// séparément.
/// </param>
/// <param name="SellerShares">Les vendeurs concernés et la part de chacun.</param>
/// <param name="Currency">La devise des montants ci-dessus.</param>
public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    Guid BuyerId,
    string Reason,
    IReadOnlyCollection<OrderSellerShare> SellerShares,
    string Currency) : DomainEvent;

/// <summary>La commande a été livrée (escrow à libérer, payout vendeur à déclencher).</summary>
public sealed record OrderDeliveredDomainEvent(Guid OrderId, Guid BuyerId) : DomainEvent;

/// <summary>LA COMMANDE EST PAYÉE MAIS PLUS EXÉCUTABLE : ELLE ATTEND UN ARBITRAGE.</summary>
public sealed record OrderUnderReviewDomainEvent(Guid OrderId, Guid BuyerId, string Reason) : DomainEvent;

/// <summary>
/// L'arbitrage a conclu à la REPRISE : la commande repart, une nouvelle course va
/// être demandée.
/// </summary>
public sealed record OrderResumedAfterReviewDomainEvent(
    Guid OrderId, Guid BuyerId, string PreviousReason) : DomainEvent;
