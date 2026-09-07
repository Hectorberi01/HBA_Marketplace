using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Merchants.Contracts;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Prévient le vendeur qu'un reversement lui a été versé.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.PayoutPaidNotificationHandler")]
public sealed class PayoutPaidNotificationHandler : IIntegrationEventHandler<PayoutPaidIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<PayoutPaidNotificationHandler> _logger;

    public PayoutPaidNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<PayoutPaidNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(PayoutPaidIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // Traduction SellerId → UserId : le jeton d'appareil est porté par le
        // COMPTE, pas par la boutique (voir SellerOrderNotificationHandler).
        var seller = await _sellers.GetSellerAsync(e.SellerId, cancellationToken);
        if (seller is null)
        {
            _logger.LogError(
                "Reversement {PayoutId} : vendeur {SellerId} introuvable — il ne sera PAS prévenu de son paiement.",
                e.PayoutId, e.SellerId);
            return;
        }

        await _dispatcher.NotifyAsync(
            seller.UserId,
            "Paiement reçu",
            $"Un reversement de {e.NetAmount:0.00} {e.Currency} vous a été versé.",
            "Payout",
            e.PayoutId,
            cancellationToken);
    }
}
