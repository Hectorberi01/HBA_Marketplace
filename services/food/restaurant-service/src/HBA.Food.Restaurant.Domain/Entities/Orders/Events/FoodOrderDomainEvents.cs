using HBA.Shared.Domain.Events;

namespace HBA.Food.Domain.Orders.Events;

/// <summary>
/// ─────────────────────────────────────────────────────────────────────────────
/// LES ÉVÉNEMENTS DE COMMANDE ET DE CUISINE (cahier des charges §19).
///
/// Le cahier les nomme en sujets Kafka — <c>food.order.accepted</c>,
/// <c>kitchen.ticket.ready</c>. Ce dépôt étant un monolithe modulaire, ils
/// passent par l'outbox transactionnel : même garantie « au moins une fois »,
/// sans second système à exploiter, et le jour de l'extraction seul l'expéditeur
/// change.
///
/// LES SUJETS « kitchen.ticket.* » N'ONT PAS D'ÉVÉNEMENTS PROPRES.
///
/// Le ticket n'étant pas un agrégat séparé, <c>kitchen.ticket.created</c> est
/// l'acceptation, <c>kitchen.ticket.started</c> le début de préparation, et
/// <c>kitchen.ticket.ready</c> la mise à disposition. Émettre les deux familles
/// aurait doublé chaque message pour décrire le même fait — et le premier
/// consommateur à s'abonner aux deux aurait tout traité deux fois.
///
/// TOUS PORTENT L'<c>OrderId</c> EN PLUS DU <c>FoodOrderId</c>. Sans lui, un
/// consommateur — Notifications, Delivery — devrait interroger Food pour savoir
/// de quelle commande commerciale il s'agit, et un événement qui oblige à
/// rappeler son émetteur n'est qu'une notification déguisée.
///
/// ET TOUS PORTENT DÉSORMAIS L'<c>Origin</c>, JUSTE APRÈS.
///
/// L'<c>OrderId</c> seul ne désigne rien : il vaut un identifiant d'order-service
/// ou de food-order-service selon le pont qui a ouvert le ticket. Un consommateur
/// qui le lisait sans savoir lequel interrogeait une base au hasard — voir
/// <see cref="FoodOrderOrigin"/> pour la liste des six gestionnaires qui le
/// faisaient, et de ce que cela cassait.
///
/// Il est en DEUXIÈME position sur les sept, uniformément. Ne le mettre que sur
/// ceux qui en ont « besoin aujourd'hui » obligerait le prochain lecteur à se
/// demander pourquoi cet événement-ci ne l'a pas — et la réponse serait « parce
/// que personne n'en avait encore eu besoin », ce qui n'est pas une règle.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed record FoodOrderReceivedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId, decimal Total, int ItemCount) : DomainEvent;

/// <summary>
/// Acceptée. Vaut aussi <c>kitchen.ticket.created</c>.
///
/// <paramref name="EstimatedPreparationMinutes"/> voyage avec : c'est la promesse
/// faite au client, et l'heure de livraison affichée en dépend.
/// </summary>
/// <param name="AcceptedByUserId">
/// NUL quand l'acceptation est AUTOMATIQUE (§3). « Personne, et c'était
/// voulu » n'est pas « on ne sait pas qui » — et le jour où un client conteste,
/// la distinction fait toute la différence.
/// </param>
public sealed record FoodOrderAcceptedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId,
    Guid? AcceptedByUserId, int EstimatedPreparationMinutes) : DomainEvent;

/// <summary>
/// Refusée, avec le motif.
///
/// Le motif est une CHAÎNE ici, pas l'énumération : cet événement sort du module,
/// et un consommateur qui devrait référencer <c>FoodRejectionReason</c> ferait la
/// dépendance que la frontière de Food interdit.
/// </summary>
public sealed record FoodOrderRejectedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId,
    string Reason, string? Comment, Guid RejectedByUserId) : DomainEvent;

/// <summary>La cuisine a commencé. Vaut <c>kitchen.ticket.started</c>.</summary>
public sealed record FoodOrderPreparationStartedDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId) : DomainEvent;

/// <summary>
/// L'ÉVÉNEMENT QUI APPELLE UN LIVREUR.
///
/// Le §24 le place au centre du flux : ReadyForPickup → HBA Delivery → HBA
/// Driver. C'est le seul de cette famille dont l'absence se verrait tout de
/// suite — des sacs qui refroidissent sur un passe sans que personne ne vienne.
/// </summary>
public sealed record FoodOrderReadyForPickupDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId, DateTime ReadyAtUtc) : DomainEvent;

public sealed record FoodOrderPickedUpDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId) : DomainEvent;

/// <summary>
/// LE SEUL DE CETTE FAMILLE QUI N'EXISTAIT PAS — ET LE RESTAURATEUR N'ÉTAIT
/// JAMAIS PAYÉ.
///
/// <c>FoodOrder.MarkDelivered</c> changeait le statut et se taisait. Le repas
/// était remis au client, le ticket passait « livré » dans la base de Food, et
/// PERSONNE hors du module ne l'apprenait : la commande commerciale restait
/// « confirmée », <c>OrderDelivered</c> n'était jamais publié, et le gain du
/// restaurateur restait « à venir » indéfiniment dans son portefeuille.
///
/// L'omission est symétrique de celle du retour de course : on avait branché la
/// fin d'une course <c>ORDER-</c> sans brancher la fin d'une course <c>FOOD-</c>
/// créée au même moment. Le même oubli, deux crans plus loin dans la chaîne.
///
/// PAS D'HORODATAGE, COMME SON JUMEAU <see cref="FoodOrderPickedUpDomainEvent"/>.
///
/// L'instant de remise fait foi chez Delivery, qui le porte déjà sur
/// <c>DeliveryCompleted</c>. Le dupliquer ici aurait imposé une colonne de plus
/// sur le ticket — donc une migration — pour une donnée dont ce module n'a aucun
/// usage.
/// </summary>
public sealed record FoodOrderDeliveredDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId) : DomainEvent;

/// <summary>
/// Annulée.
///
/// <paramref name="WasInKitchen"/> porte la seule chose qui compte pour le
/// restaurant : des denrées avaient-elles été engagées ? Une annulation avant
/// acceptation ne coûte rien ; la même trente minutes plus tard coûte un repas,
/// et c'est ce qui fonde une éventuelle indemnisation.
/// </summary>
public sealed record FoodOrderCancelledDomainEvent(
    Guid FoodOrderId, FoodOrderOrigin Origin, Guid OrderId, Guid RestaurantId, string? Reason, bool WasInKitchen) : DomainEvent;
