using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.FoodOrders.Application.Orders.EventHandlers;

namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LES DEUX GESTIONNAIRES QUI TIENNENT L'ARGENT.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.ConfirmMealOrderOnPaymentCapturedHandler")]
public sealed class ConfirmMealOrderOnPaymentCapturedHandler
    : IIntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    /// <summary>La valeur que porte un paiement de repas.</summary>
    internal const string TypeDeCommande = "FOOD";

    private readonly ISender _sender;
    private readonly ILogger<ConfirmMealOrderOnPaymentCapturedHandler> _logger;

    public ConfirmMealOrderOnPaymentCapturedHandler(
        ISender sender, ILogger<ConfirmMealOrderOnPaymentCapturedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(integrationEvent.OrderType, TypeDeCommande, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var resultat = await _sender.Send(
            new ConfirmMealOrderPaymentCommand(integrationEvent.OrderId), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "confirmer le paiement de la commande de repas — LE CLIENT A ÉTÉ DÉBITÉ",
            integrationEvent.OrderId, integrationEvent.PaymentId);
    }
}

/// <summary>À l'échec du paiement, la commande est annulée.</summary>
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.CancelMealOrderOnPaymentFailedHandler")]
public sealed class CancelMealOrderOnPaymentFailedHandler
    : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelMealOrderOnPaymentFailedHandler> _logger;

    public CancelMealOrderOnPaymentFailedHandler(
        ISender sender, ILogger<CancelMealOrderOnPaymentFailedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        PaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                integrationEvent.OrderType,
                ConfirmMealOrderOnPaymentCapturedHandler.TypeDeCommande,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // RequesterId laissé nul : c'est le système qui annule, pas le client.
        var resultat = await _sender.Send(
            new CancelMealOrderCommand(
                integrationEvent.OrderId, $"Paiement échoué : {integrationEvent.Reason}"),
            cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "annuler la commande de repas après échec du paiement",
            integrationEvent.OrderId);
    }
}
