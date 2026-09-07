using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le gain d'une course vient d'être crédité → le livreur l'apprend.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.DriverEarningCreditedNotificationHandler")]
public sealed class DriverEarningCreditedNotificationHandler
    : IIntegrationEventHandler<DriverEarningCreditedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IDeliveryModuleApi _deliveries;
    private readonly ILogger<DriverEarningCreditedNotificationHandler> _logger;

    public DriverEarningCreditedNotificationHandler(
        NotificationDispatcher dispatcher,
        IDeliveryModuleApi deliveries,
        ILogger<DriverEarningCreditedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _deliveries = deliveries;
        _logger = logger;
    }

    public async Task HandleAsync(
        DriverEarningCreditedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // Traduction DriverId → UserId : le portefeuille est indexé sur le livreur,
        // le jeton d'appareil sur le COMPTE. L'événement ne porte pas le second,
        // comme les événements de course — voir `IDeliveryModuleApi`.
        var livreur = await _deliveries.GetDriverAccountAsync(e.DriverId, cancellationToken);

        if (livreur is null)
        {
            // ON N'ÉCHOUE PAS : L'ARGENT EST DÉJÀ AU PORTEFEUILLE.
            _logger.LogError(
                "Gain crédité sur la course {DeliveryId} : livreur {DriverId} introuvable — "
                + "il ne sera PAS prévenu de son paiement.",
                e.DeliveryId, e.DriverId);

            return;
        }

        await _dispatcher.NotifyAsync(
            livreur.UserId,
            "Course payée",
            $"Votre gain de {e.Amount:0.00} {e.Currency} a été ajouté à votre solde.",
            "Delivery",
            e.DeliveryId,
            cancellationToken,
            alsoEmail: false);
    }
}
