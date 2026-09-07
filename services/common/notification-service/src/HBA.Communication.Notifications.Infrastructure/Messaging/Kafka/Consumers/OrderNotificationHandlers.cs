using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Notifie l'acheteur que sa commande est passée (en attente de paiement).</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.OrderPlacedNotificationHandler")]
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
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.OrderConfirmedNotificationHandler")]
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
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.OrderCancelledNotificationHandler")]
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
/// La commande est PRISE EN CHARGE : un incident de livraison empêche de la mener à
/// bien, et l'équipe HBA s'en occupe.
/// </summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.OrderUnderReviewNotificationHandler")]
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

/// <summary>L'incident est levé : la commande repart.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.OrderResumedAfterReviewNotificationHandler")]
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
