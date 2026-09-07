using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.FoodOrders.Application.Orders.EventHandlers;

namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>CE QUE LA CUISINE DÉCIDE, ET CE QUE LA COMMANDE EN FAIT.</summary>
internal static class TicketDeRepas
{
    /// <summary>Ce ticket vient-il d'une `MealOrder` ?</summary>
    public static bool Nous(string? origine)
        => string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase);
}
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.CancelMealOrderOnKitchenRejectionHandler")]
public sealed class CancelMealOrderOnKitchenRejectionHandler
    : IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelMealOrderOnKitchenRejectionHandler> _logger;

    public CancelMealOrderOnKitchenRejectionHandler(
        ISender sender, ILogger<CancelMealOrderOnKitchenRejectionHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // Le refus d'un ticket de la marketplace est traité par
        // `CancelOrderOnFoodOrderRejectedHandler`, chez order-service.
        if (!TicketDeRepas.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var motif = string.IsNullOrWhiteSpace(integrationEvent.Comment)
            ? integrationEvent.Reason
            : $"{integrationEvent.Reason} — {integrationEvent.Comment}";

        var resultat = await _sender.Send(
            new RejectMealOrderByRestaurantCommand(integrationEvent.OrderId, motif), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "annuler la commande après refus du restaurant — LE CLIENT A ÉTÉ DÉBITÉ",
            integrationEvent.OrderId, integrationEvent.FoodOrderId);
    }
}

/// <summary>Le ticket a été annulé côté cuisine.</summary>
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.CancelMealOrderOnKitchenCancellationHandler")]
public sealed class CancelMealOrderOnKitchenCancellationHandler
    : IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelMealOrderOnKitchenCancellationHandler> _logger;

    public CancelMealOrderOnKitchenCancellationHandler(
        ISender sender, ILogger<CancelMealOrderOnKitchenCancellationHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!TicketDeRepas.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var motif = string.IsNullOrWhiteSpace(integrationEvent.Reason)
            ? "Annulée par le restaurant."
            : integrationEvent.Reason;

        var resultat = await _sender.Send(
            new RejectMealOrderByRestaurantCommand(integrationEvent.OrderId, motif), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "annuler la commande après annulation du ticket de cuisine",
            integrationEvent.OrderId, integrationEvent.FoodOrderId);
    }
}

/// <summary>Le repas a été remis au client : la commande se clôt.</summary>
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.MarkMealOrderDeliveredOnKitchenDeliveryHandler")]
public sealed class MarkMealOrderDeliveredOnKitchenDeliveryHandler
    : IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkMealOrderDeliveredOnKitchenDeliveryHandler> _logger;

    public MarkMealOrderDeliveredOnKitchenDeliveryHandler(
        ISender sender, ILogger<MarkMealOrderDeliveredOnKitchenDeliveryHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!TicketDeRepas.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var resultat = await _sender.Send(
            new MarkMealOrderDeliveredCommand(integrationEvent.OrderId), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "clore la commande après remise du repas — L'ESCROW RESTE GELÉ ET LE RESTAURATEUR N'EST PAS RÉGLÉ",
            integrationEvent.OrderId, integrationEvent.FoodOrderId);
    }
}
