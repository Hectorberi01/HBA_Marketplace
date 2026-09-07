using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Orders.Application.Orders;
using HBA.Orders.Application.Orders.EventHandlers;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Suite du Saga côté commande : à la capture du paiement, confirme la commande
/// (solde le stock) ; à l'échec, l'annule (libère le stock).
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Orders.Application.Orders.EventHandlers.ConfirmOrderOnPaymentCapturedHandler")]
public sealed class ConfirmOrderOnPaymentCapturedHandler : IIntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<ConfirmOrderOnPaymentCapturedHandler> _logger;

    public ConfirmOrderOnPaymentCapturedHandler(
        ISender sender, ILogger<ConfirmOrderOnPaymentCapturedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var resultat = await _sender.Send(
            new ConfirmOrderPaymentCommand(integrationEvent.OrderId, integrationEvent.PaymentId), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "confirmer le paiement de la commande — L'ACHETEUR A ÉTÉ DÉBITÉ",
            integrationEvent.OrderId, integrationEvent.PaymentId);
    }
}

/// <summary>À l'échec du paiement, annule la commande et libère les réservations.</summary>
[NomDeConsommateur("HBA.Orders.Application.Orders.EventHandlers.CancelOrderOnPaymentFailedHandler")]
public sealed class CancelOrderOnPaymentFailedHandler : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelOrderOnPaymentFailedHandler> _logger;

    public CancelOrderOnPaymentFailedHandler(
        ISender sender, ILogger<CancelOrderOnPaymentFailedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        PaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // RequesterId laissé nul : c'est le système qui annule, pas l'acheteur.
        var resultat = await _sender.Send(
            new CancelOrderCommand(
                integrationEvent.OrderId, $"Paiement échoué : {integrationEvent.Reason}"),
            cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "annuler la commande après échec du paiement — LES RÉSERVATIONS DE STOCK RESTENT POSÉES",
            integrationEvent.OrderId);
    }
}
