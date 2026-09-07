using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le remboursement est ACCEPTÉ — l'argent n'est pas encore parti.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.ReturnRefundApprovedNotificationHandler")]
public sealed class ReturnRefundApprovedNotificationHandler : IIntegrationEventHandler<ReturnRefundApprovedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public ReturnRefundApprovedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(ReturnRefundApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId,
            "Remboursement accepté",
            $"Votre remboursement de {e.RefundAmount:0.00} {e.Currency} est accepté. " +
            "Le versement est en cours ; vous serez prévenu dès qu'il sera effectué.",
            "Return",
            e.ReturnRequestId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// L'argent est PARTI. On prévient l'acheteur… et le vendeur, qui vient d'être
/// débité.
/// </summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.ReturnRefundedNotificationHandler")]
public sealed class ReturnRefundedNotificationHandler : IIntegrationEventHandler<ReturnRefundedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<ReturnRefundedNotificationHandler> _logger;

    public ReturnRefundedNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<ReturnRefundedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(ReturnRefundedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // L'ACHETEUR d'abord : c'est lui qui attend son argent.
        await _dispatcher.NotifyAsync(
            e.BuyerId,
            "Remboursement effectué",
            $"Votre remboursement de {e.RefundAmount:0.00} {e.Currency} a été versé. " +
            "Selon votre opérateur, il peut apparaître sous 24 à 72 heures.",
            "Return",
            e.ReturnRequestId,
            cancellationToken,
            alsoEmail: true);

        // LE VENDEUR ensuite : son solde vient d'être débité.
        try
        {
            var seller = await _sellers.GetSellerAsync(e.SellerId, cancellationToken);
            if (seller is null)
            {
                _logger.LogError(
                    "Remboursement {ReturnRequestId} : vendeur {SellerId} introuvable — il ne sera pas informé du débit.",
                    e.ReturnRequestId, e.SellerId);
                return;
            }

            await _dispatcher.NotifyAsync(
                seller.UserId,
                "Retour remboursé au client",
                $"Un retour a été remboursé : {e.RefundAmount:0.00} {e.Currency}. " +
                "Le montant correspondant a été déduit de votre solde.",
                "Return",
                e.ReturnRequestId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le vendeur n'est pas joignable : ce n'est pas une raison pour faire
            // échouer le handler.
            _logger.LogError(
                ex,
                "Remboursement {ReturnRequestId} : échec de la notification du vendeur {SellerId}.",
                e.ReturnRequestId, e.SellerId);
        }
    }
}

/// <summary>Le paiement lui-même a été remboursé chez le prestataire.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.PaymentRefundedNotificationHandler")]
public sealed class PaymentRefundedNotificationHandler : IIntegrationEventHandler<PaymentRefundedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public PaymentRefundedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(PaymentRefundedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.BuyerId,
            "Remboursement effectué",
            $"Votre paiement de {e.Amount:0.00} {e.Currency} a été remboursé. " +
            "Selon votre opérateur, le montant peut apparaître sous 24 à 72 heures.",
            "Order",
            e.OrderId,
            cancellationToken,
            alsoEmail: true);
}
