using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Financial.Payments.Application.Payments.Commands;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Financial.Payments.Application.Payments;
using HBA.Financial.Payments.Application.Payments.EventHandlers;

// `IPaymentsModuleApi` EXISTE AUSSI DANS `HBA.Payments.Contracts` (partagé).
using HBA.Financial.Payments.Contracts;

namespace HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Commande annulée → paiement remboursé.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Financial.Payments.Application.Payments.EventHandlers.RefundPaymentOnOrderCancelledHandler")]
public sealed class RefundPaymentOnOrderCancelledHandler
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IPaymentsModuleApi _payments;
    private readonly ILogger<RefundPaymentOnOrderCancelledHandler> _logger;

    public RefundPaymentOnOrderCancelledHandler(
        ISender sender,
        IPaymentsModuleApi payments,
        ILogger<RefundPaymentOnOrderCancelledHandler> logger)
    {
        _sender = sender;
        _payments = payments;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var paiement = await _payments.GetPaymentByOrderAsync(e.OrderId, cancellationToken);

        if (paiement is null)
        {
            _logger.LogInformation(
                "Commande {OrderId} annulée : aucun paiement à rembourser.", e.OrderId);

            return;
        }

        // SEUL UN PAIEMENT ENCAISSÉ SE REMBOURSE.
        if (!string.Equals(paiement.Status, "Captured", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Commande {OrderId} annulée : paiement {PaymentId} dans l'état {Statut}, "
                + "aucun remboursement.",
                e.OrderId, paiement.Id, paiement.Status);

            return;
        }

        var remboursement = await _sender.Send(new RefundPaymentCommand(paiement.Id), cancellationToken);

        if (remboursement.IsFailure)
        {
            var erreur = remboursement.Error;

            // PANNE PASSAGÈRE : ON LÈVE, ET C'EST LE SEUL CAS OÙ ON LÈVE.
            if (erreur.Type == ErrorType.DependencyUnavailable)
            {
                _logger.LogError(
                    "Remboursement INTERROMPU pour la commande annulée {OrderId} — paiement "
                    + "{PaymentId}, {Code} : {Message}. Prestataire injoignable : rejeu demandé.",
                    e.OrderId, paiement.Id, erreur.Code, erreur.Message);

                throw new InvalidOperationException(
                    $"Remboursement interrompu pour la commande {e.OrderId} : "
                    + $"{erreur.Code} — {erreur.Message}");
            }

            // REFUS DU PRESTATAIRE : ON NE LÈVE PAS. LEVER SATURAIT LA FILE.
            _logger.LogCritical(
                "Paiement {PaymentId} NON REMBOURSÉ pour la commande annulée {OrderId} — "
                + "{Code} : {Message}. Le client est débité pour une commande annulée et aucun "
                + "rejeu ne corrigera cela : REMBOURSEMENT MANUEL REQUIS chez {Prestataire}.",
                paiement.Id, e.OrderId, erreur.Code, erreur.Message, paiement.Provider);

            return;
        }

        _logger.LogInformation(
            "Paiement {PaymentId} remboursé pour la commande annulée {OrderId} ({Motif}).",
            paiement.Id, e.OrderId, e.Reason);
    }
}
