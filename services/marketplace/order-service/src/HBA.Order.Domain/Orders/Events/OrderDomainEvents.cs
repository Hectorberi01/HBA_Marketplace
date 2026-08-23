using HBA.Shared.Domain.Events;

namespace HBA.Orders.Domain.Orders.Events;

/// <summary>
/// Part d'UN vendeur dans une commande : ce qu'il a vendu, et pour combien.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE TYPE EXISTE
///
/// Une commande peut contenir les produits de PLUSIEURS vendeurs. Chacun ne doit
/// connaître que SA part.
///
/// Envoyer à un vendeur le total de la commande — qui inclut les articles des
/// autres — lui ferait croire qu'il a vendu pour un montant qui n'est pas le sien.
/// Il l'apprendrait au moment d'être payé, et la confiance ne s'en remettrait pas.
/// C'est aussi, accessoirement, une fuite d'information commerciale entre
/// concurrents.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
/// <param name="SellerId">Le vendeur concerné.</param>
/// <param name="ItemCount">Nombre d'articles de CE vendeur (somme des quantités).</param>
/// <param name="Amount">Montant dû à CE vendeur pour cette commande.</param>
public sealed record OrderSellerShare(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>Une commande a été placée (stock réservé, en attente de paiement).</summary>
public sealed record OrderPlacedDomainEvent(Guid OrderId, Guid BuyerId, Guid CartId, decimal GrandTotal, string Currency) : DomainEvent;

/// <summary>
/// La commande a été confirmée (paiement encaissé, stock soldé).
///
/// C'est LE moment où les vendeurs sont prévenus : la commande est payée, donc
/// réelle. Prévenir plus tôt (à « OrderPlaced ») reviendrait à les alerter pour des
/// paniers dont le paiement peut encore échouer — et à les pousser à préparer, voire
/// expédier, une marchandise jamais payée.
///
/// L'événement transporte donc la répartition par vendeur : sans elle, aucun
/// consommateur en aval ne pourrait savoir QUI prévenir, ni de QUOI.
/// </summary>
/// <param name="Kind">
/// « Goods » ou « Food ». Sans lui, les sept consommateurs de la confirmation
/// traitent un repas comme un colis — Shipping en tête, qui créerait une
/// expédition attribuée au vendeur « 00000000-… ».
/// </param>
/// <param name="RestaurantId">L'établissement qui prépare, ou null hors restauration.</param>
public sealed record OrderConfirmedDomainEvent(
    Guid OrderId,
    Guid BuyerId,
    string Currency,
    string? PromotionCode,
    IReadOnlyCollection<OrderSellerShare> SellerShares,
    string Kind,
    Guid? RestaurantId) : DomainEvent;

/// <summary>La commande a été annulée (réservations libérées).</summary>
public sealed record OrderCancelledDomainEvent(Guid OrderId, Guid BuyerId, string Reason) : DomainEvent;

/// <summary>La commande a été livrée (escrow à libérer, payout vendeur à déclencher).</summary>
public sealed record OrderDeliveredDomainEvent(Guid OrderId, Guid BuyerId) : DomainEvent;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA COMMANDE EST PAYÉE MAIS PLUS EXÉCUTABLE : ELLE ATTEND UN ARBITRAGE.
///
/// CE N'EST PAS UNE ANNULATION, ET LE CONSOMMATEUR NE DOIT PAS LA TRAITER
/// COMME TELLE.
///
/// La vente est vivante : l'argent est encaissé, le stock décrémenté, et une
/// course annulée est le plus souvent réattribuable. Le seul consommateur
/// attendu est le service de notification — pour DIRE à l'acheteur que c'est
/// pris en charge. Un consommateur qui rembourserait ici détruirait des ventes
/// récupérables, sans possibilité de revenir en arrière.
///
/// <paramref name="Reason"/> voyage en clair : il finit dans la file
/// d'arbitrage, lue par quelqu'un qui ne connaît pas ce code.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record OrderUnderReviewDomainEvent(Guid OrderId, Guid BuyerId, string Reason) : DomainEvent;

/// <summary>
/// L'arbitrage a conclu à la REPRISE : la commande repart, une nouvelle course
/// va être demandée.
///
/// DISTINCT DE `OrderConfirmed`, ET IL NE FAUT SURTOUT PAS LES CONFONDRE. La
/// confirmation ouvre le ticket de cuisine, décompte le coupon, comptabilise les
/// gains et prévient les vendeurs. Tout cela a DÉJÀ eu lieu ; le rejouer
/// paierait deux fois. Celui-ci ne dit qu'une chose : la suspension est levée.
/// </summary>
public sealed record OrderResumedAfterReviewDomainEvent(
    Guid OrderId, Guid BuyerId, string PreviousReason) : DomainEvent;
