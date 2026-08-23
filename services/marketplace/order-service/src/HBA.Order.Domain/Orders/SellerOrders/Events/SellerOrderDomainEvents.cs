using HBA.Shared.Domain.Events;

namespace HBA.Orders.Domain.Orders.SellerOrders.Events;

/// <summary>
/// Une ligne que le vendeur n'honorera pas : de quoi la reprendre en stock et la
/// rembourser.
/// </summary>
/// <remarks>
/// <paramref name="ShipFromLocationId"/> N'EST PAS DU REMPLISSAGE.
///
/// Inventory travaille par (SKU, emplacement, commande) —
/// `ReleaseReservationAsync` et `ConfirmReservationAsync` n'ont aucun autre moyen
/// de désigner une réservation. Un événement de refus sans l'emplacement laisse
/// donc son futur consommateur incapable de rendre le stock, ce qui est
/// exactement la moitié du travail qu'il devra faire.
/// </remarks>
public sealed record SellerOrderRefusedLine(
    Guid OrderLineId,
    Guid ProductId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal LineTotal);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN VENDEUR N'HONORERA PAS SA PART D'UNE COMMANDE DÉJÀ PAYÉE.
///
/// CET ÉVÉNEMENT N'A AUJOURD'HUI AUCUN CONSOMMATEUR. UN REFUS VENDEUR NE
/// REMBOURSE ENCORE PERSONNE.
///
/// Il faut le dire ici plutôt que de le laisser découvrir : le client a payé,
/// une part de sa commande ne viendra pas, et à cette heure RIEN dans la
/// plateforme n'agit dessus. Trois gestes manquent, et aucun n'appartient à
/// order-service :
///
///   • LIBÉRER LE STOCK de la part refusée. Il a été SOLDÉ à la confirmation
///     (`ConfirmReservationAsync`) : ce n'est donc pas une réservation à rendre,
///     c'est de la marchandise à remettre en rayon — un geste d'exploitation
///     dans inventory-service ;
///   • REMBOURSER LA PART. financial-service sait rembourser une COMMANDE
///     (`OrderCancelled`) ; il ne sait pas rembourser une FRACTION de commande,
///     et le montant partiel n'a aujourd'hui aucun chemin ;
///   • PRÉVENIR L'ACHETEUR, dans communication-service.
///
/// Écrire ces trois consommateurs est hors du périmètre de ce lot — ils vivent
/// dans trois autres services. Ce qui EST dans le périmètre, c'est de porter
/// assez d'information pour qu'ils soient écrivables sans revenir ici : la
/// commande, l'acheteur, le vendeur, les lignes avec leur emplacement
/// d'expédition, le montant, la devise et le motif.
///
/// UN SEUL TYPE POUR LE REFUS ET POUR L'ANNULATION, ET C'EST UN CHOIX.
///
/// Refuser avant de s'engager et se dédire après ne sont pas le même geste pour
/// le vendeur — d'où deux permissions distinctes, `ORDER_REJECT` et
/// `ORDER_CANCEL`. Mais pour le consommateur, la conséquence est IDENTIQUE au
/// mot près : cette part ne viendra pas, rendez le stock, rendez l'argent,
/// prévenez le client. Deux types auraient obligé chacun des trois services
/// futurs à s'abonner deux fois et à écrire deux fois le même gestionnaire — et
/// le jour où l'un des deux abonnements serait oublié, la moitié des refus
/// passerait à travers sans que rien n'échoue.
///
/// <paramref name="Outcome"/> porte la distinction pour qui en a besoin
/// (« Rejected » / « Cancelled »).
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Amount">
/// Le montant PAYÉ pour cette part — somme des <c>LineTotal</c> du vendeur,
/// remises comprises.
///
/// CE N'EST PAS LE MONTANT À REMBOURSER, ET LA NUANCE COÛTE DE L'ARGENT. Le
/// frais de port est porté par la COMMANDE, pas par le vendeur (voir
/// `OrderMapper.ToSellerSummary`) : si le refus vide la commande entière, le
/// client doit aussi récupérer la livraison, et ce calcul-là appartient à qui
/// possède le paiement.
/// </param>
public sealed record SellerOrderRefusedDomainEvent(
    Guid SellerOrderId,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    string Currency,
    string Outcome,
    string Reason,
    decimal Amount,
    IReadOnlyCollection<SellerOrderRefusedLine> Lines) : DomainEvent;
