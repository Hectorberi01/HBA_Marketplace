using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Une course est proposée à un livreur → il en est averti.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.NotifyDriverOnDeliveryAssignedHandler")]
public sealed class NotifyDriverOnDeliveryAssignedHandler
    : IIntegrationEventHandler<DeliveryAssignedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IDeliveryModuleApi _deliveries;
    private readonly ILogger<NotifyDriverOnDeliveryAssignedHandler> _logger;

    public NotifyDriverOnDeliveryAssignedHandler(
        NotificationDispatcher dispatcher,
        IDeliveryModuleApi deliveries,
        ILogger<NotifyDriverOnDeliveryAssignedHandler> logger)
    {
        _dispatcher = dispatcher;
        _deliveries = deliveries;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryAssignedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var livreur = await _deliveries.GetDriverAccountAsync(e.DriverId, cancellationToken);

        if (livreur is null)
        {
            _logger.LogWarning(
                "Proposition non notifiée : livreur {DriverId} introuvable pour la course {DeliveryId}.",
                e.DriverId, e.DeliveryId);

            return;
        }

        // PAS D'E-MAIL. Une proposition dure quarante-cinq secondes ; un courriel
        // arrive après la bataille et encombre une boîte pour rien.
        await _dispatcher.NotifyAsync(
            livreur.UserId,
            "Nouvelle course",
            "Une course vous est proposée. Vous avez 45 secondes pour l'accepter.",
            "Delivery",
            e.DeliveryId,
            cancellationToken,
            alsoEmail: false);
    }
}
