using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>Notifie l'acheteur que sa commande est passée (en attente de paiement).</summary>
public sealed class OrderPlacedNotificationHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderPlacedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId, "Commande enregistrée",
            $"Votre commande est enregistrée pour {e.GrandTotal:0.00} {e.Currency}. En attente de paiement.",
            "Order", e.OrderId, cancellationToken, alsoEmail: true);
}

/// <summary>Notifie l'acheteur que sa commande est confirmée.</summary>
public sealed class OrderConfirmedNotificationHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderConfirmedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId, "Commande confirmée",
            "Votre paiement a été reçu et votre commande est confirmée. Préparation en cours.",
            "Order", e.OrderId, cancellationToken, alsoEmail: true);
}

/// <summary>Notifie l'acheteur que sa commande est annulée.</summary>
public sealed class OrderCancelledNotificationHandler : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderCancelledNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId, "Commande annulée",
            $"Votre commande a été annulée. Motif : {e.Reason}",
            "Order", e.OrderId, cancellationToken, alsoEmail: true);
}

/// <summary>
/// La commande est PRISE EN CHARGE : un incident de livraison empêche de la
/// mener à bien, et l'équipe HBA s'en occupe.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE MESSAGE EST CELUI QUI ÉVITE UNE RÉCLAMATION, ET SA FORMULATION COMPTE
///    AUTANT QUE SON EXISTENCE.
///
/// Avant lui, une commande devenue inexécutable restait « confirmée » sans un
/// mot : le client attendait un colis que personne n'apportait, et découvrait le
/// problème au bout de plusieurs jours, en appelant. Argent encaissé, stock
/// décrémenté, escrow gelé, et aucune trace côté acheteur.
///
/// NE JAMAIS LAISSER CROIRE À UNE ANNULATION.
///
/// La vente est VIVANTE : une course annulée se réattribue le plus souvent, et
/// l'exploitation va très probablement relancer. Écrire « votre commande ne peut
/// pas être livrée » ferait exiger un remboursement à quelqu'un qui recevra son
/// colis le lendemain — et transformerait une reprise réussie en litige.
///
/// Le message dit donc trois choses, dans cet ordre : il y a un incident, nous
/// l'avons vu, nous revenons vers vous. Le motif technique n'y figure pas :
/// « expédition depuis 2 lieux » n'apprend rien à un acheteur et l'inquiète.
///
/// DOUBLÉ PAR COURRIEL. Le client n'ouvrira pas forcément l'application, et
/// c'est précisément le moment où il doit être joint.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class OrderUnderReviewNotificationHandler
    : IIntegrationEventHandler<OrderUnderReviewIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderUnderReviewNotificationHandler(NotificationDispatcher dispatcher)
        => _dispatcher = dispatcher;

    public Task HandleAsync(
        OrderUnderReviewIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId,
            "Votre commande est prise en charge",
            "Un incident est survenu sur la livraison de votre commande. Notre équipe la traite "
            + "en priorité et vous recontacte très vite. Votre paiement reste acquis : rien ne "
            + "vous sera redemandé, et vous serez remboursé si la livraison s'avérait impossible.",
            "Order",
            e.OrderId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// L'incident est levé : la commande repart.
/// </summary>
/// <remarks>
/// SANS CE SECOND MESSAGE, LE PREMIER SE RETOURNE CONTRE NOUS.
///
/// « Nous vous recontactons très vite » suivi de rien vaut moins que le silence :
/// c'est une promesse non tenue, et c'est ce qui déclenche l'appel au support que
/// le premier message devait éviter.
/// </remarks>
public sealed class OrderResumedAfterReviewNotificationHandler
    : IIntegrationEventHandler<OrderResumedAfterReviewIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public OrderResumedAfterReviewNotificationHandler(NotificationDispatcher dispatcher)
        => _dispatcher = dispatcher;

    public Task HandleAsync(
        OrderResumedAfterReviewIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId,
            "Votre commande repart",
            "L'incident sur votre livraison est réglé. Un nouveau livreur est recherché pour "
            + "votre commande ; vous serez prévenu dès qu'il sera en route.",
            "Order",
            e.OrderId,
            cancellationToken,
            alsoEmail: true);
}
