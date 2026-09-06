using HBA.Shared.IntegrationEvents;
using HBA.Ordering.Contracts;
using HBA.Shipping.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
//
// Il vivait dans `HBA.Communication.Notifications.Application.Notifications.EventHandlers` et y resolvait ses voisins SANS `using` : le
// compilateur cherche d'abord dans les espaces de noms englobants. Descendu
// dans `Messaging/Kafka/Consumers`, il a perdu ce voisinage — d'ou les lignes
// ci-dessous, qui rendent explicite ce qui etait implicite.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Notifie l'acheteur qu'un colis est expédié. L'event d'expédition ne porte pas
/// le destinataire : on le résout via Ordering (Contracts).
/// </summary>
public sealed class ShipmentShippedNotificationHandler : IIntegrationEventHandler<ShipmentShippedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orderingModuleApi;

    public ShipmentShippedNotificationHandler(NotificationDispatcher dispatcher, IOrderingModuleApi orderingModuleApi)
    {
        _dispatcher = dispatcher;
        _orderingModuleApi = orderingModuleApi;
    }

    public async Task HandleAsync(ShipmentShippedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var order = await _orderingModuleApi.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            order.BuyerId, "Colis expédié",
            $"Un colis de votre commande a été expédié via {e.Carrier}. Suivi : {e.TrackingNumber}.",
            "Shipment", e.ShipmentId, cancellationToken, alsoEmail: true);
    }
}

/// <summary>Notifie l'acheteur qu'un colis est livré (destinataire résolu via Ordering).</summary>
public sealed class ShipmentDeliveredNotificationHandler : IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orderingModuleApi;

    public ShipmentDeliveredNotificationHandler(NotificationDispatcher dispatcher, IOrderingModuleApi orderingModuleApi)
    {
        _dispatcher = dispatcher;
        _orderingModuleApi = orderingModuleApi;
    }

    public async Task HandleAsync(ShipmentDeliveredIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var order = await _orderingModuleApi.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            order.BuyerId, "Colis livré",
            "Un colis de votre commande a été livré. Vous pouvez maintenant laisser un avis.",
            "Shipment", e.ShipmentId, cancellationToken, alsoEmail: true);
    }
}
