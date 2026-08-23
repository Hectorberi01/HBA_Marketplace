using HBA.Shared.Domain.Events;

namespace HBA.FoodOrders.Domain.Orders.Events;

/// <summary>La commande est enregistrée et attend son paiement.</summary>
public sealed record MealOrderPlacedDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, Guid CartId, decimal GrandTotal, string Currency) : DomainEvent;

/// <summary>
/// Le paiement est encaissé : la cuisine peut recevoir le ticket.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// IL PORTE SES LIGNES, LÀ OÙ SON ANCÊTRE OBLIGEAIT À UN ALLER-RETOUR gRPC.
///
/// `ReceiveFoodOrderOnOrderConfirmedHandler` recevait `OrderConfirmed`, testait
/// `Kind == "Food"`, rappelait order-service par gRPC pour obtenir les lignes,
/// puis filtrait celles dont le `Kind` valait « Food ». Trois pas, dont deux
/// n'existaient que parce que l'événement servait deux univers et ne pouvait donc
/// rien porter de spécifique à l'un.
///
/// AUCUNE « PART VENDEUR » ICI.
///
/// `OrderConfirmedDomainEvent` transportait une répartition par vendeur, vide par
/// construction pour un repas — et il avait fallu un filtre explicite dans
/// `BuildSellerShares` pour éviter qu'elle ne produise UNE part attribuée au
/// vendeur « 00000000-… », sur laquelle trois consommateurs auraient agi. La
/// rémunération du restaurant passe par son dossier de reversement, pas par la
/// répartition vendeur de la marketplace.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MealOrderConfirmedDomainEvent(
    Guid OrderId,
    Guid BuyerId,
    Guid RestaurantId,
    decimal GrandTotal,
    decimal ShippingFee,
    string Currency,
    string? PromotionCode,
    string? DeliveryQuoteId,
    string? CustomerNote,
    IReadOnlyCollection<MealOrderConfirmedLine> Lines) : DomainEvent;

/// <summary>
/// Une ligne telle qu'elle voyage vers la cuisine.
///
/// UN RECORD DU DOMAINE, RECOPIÉ ENSUITE DANS LE CONTRAT PUBLIC.
///
/// Deux types portent la même idée, et c'est délibéré : le contrat exposé aux
/// autres services ne doit pas dépendre du modèle interne, sans quoi le moindre
/// remaniement du domaine casserait la cuisine, le paiement et les
/// notifications. C'est le même parti pris que `OrderSellerShare` côté
/// marketplace.
/// </summary>
public sealed record MealOrderConfirmedLine(
    Guid LineId,
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    IReadOnlyCollection<(Guid GroupId, Guid OptionId)> Options);

/// <summary>La commande a été annulée. financial-service rembourse en le consommant.</summary>
public sealed record MealOrderCancelledDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, string Reason) : DomainEvent;

/// <summary>Le repas a été remis au client : escrow à libérer, restaurateur à régler.</summary>
public sealed record MealOrderDeliveredDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId) : DomainEvent;

/// <summary>
/// La commande est payée mais plus exécutable : elle attend un arbitrage.
///
/// CE N'EST PAS UNE ANNULATION, ET LE CONSOMMATEUR NE DOIT PAS LA TRAITER
/// COMME TELLE. Le seul consommateur attendu est la notification — pour DIRE au
/// client que c'est pris en charge. Un consommateur qui rembourserait ici
/// détruirait des ventes récupérables.
/// </summary>
public sealed record MealOrderUnderReviewDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, string Reason) : DomainEvent;

/// <summary>
/// L'arbitrage a conclu à la REPRISE.
///
/// DISTINCT DE LA CONFIRMATION, ET IL NE FAUT SURTOUT PAS LES CONFONDRE. La
/// confirmation ouvre le ticket de cuisine, décompte le coupon et comptabilise
/// les gains. Tout cela a DÉJÀ eu lieu ; le rejouer ferait préparer le repas une
/// seconde fois et brûlerait un second coupon.
/// </summary>
public sealed record MealOrderResumedAfterReviewDomainEvent(
    Guid OrderId, Guid BuyerId, Guid RestaurantId, string PreviousReason) : DomainEvent;
