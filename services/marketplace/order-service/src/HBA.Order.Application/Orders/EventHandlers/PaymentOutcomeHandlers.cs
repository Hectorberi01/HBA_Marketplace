using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
using HBA.Financial.Payments.Contracts.IntegrationEvents;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>
/// Suite du Saga côté commande : à la capture du paiement, confirme la commande
/// (solde le stock) ; à l'échec, l'annule (libère le stock). Ordering ne dépend
/// que des Contracts de Payments.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CES DEUX GESTIONNAIRES SONT LES DEUX QUI TIENNENT L'ARGENT.
///
/// Ils s'écrivaient en une flèche, sans inspecter le résultat :
///
///     => _sender.Send(new ConfirmOrderPaymentCommand(e.OrderId), ct);
///
/// La commande pouvait refuser — commande introuvable, déjà annulée, stock
/// insuffisant — et le message Kafka était acquitté quand même. Paiement
/// encaissé, commande jamais confirmée, silence complet. Sur l'échec de
/// paiement, même chose à l'envers : le stock réservé n'était jamais libéré.
///
/// C'était invisible sur une carte des événements : le câblage était correct.
/// Seul l'EFFET manquait.
///
/// La règle de tri — journaliser un échec d'état, lever sur une cause
/// passagère — vit dans <see cref="SagaOutcome"/>, avec son argumentaire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

/// <summary>
/// À l'échec du paiement, annule la commande et libère les réservations.
/// </summary>
/// <remarks>
/// L'ÉCHEC ICI IMMOBILISE DU STOCK.
///
/// Sans annulation, les réservations posées au checkout restent en place :
/// des articles disponibles cessent d'être vendables, sans que rien ne
/// l'explique. C'est moins spectaculaire qu'un débit sans commande, et cela se
/// découvre encore plus tard.
/// </remarks>
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
