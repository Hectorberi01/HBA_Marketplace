using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Orders.Application.Orders;
using HBA.Orders.Application.Orders.EventHandlers;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le repas est remis au client → la commande commerciale passe « livrée ».</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Orders.Application.Orders.EventHandlers.MarkOrderDeliveredOnFoodOrderDeliveredHandler")]
public sealed class MarkOrderDeliveredOnFoodOrderDeliveredHandler
    : IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkOrderDeliveredOnFoodOrderDeliveredHandler> _logger;

    public MarkOrderDeliveredOnFoodOrderDeliveredHandler(
        ISender sender, ILogger<MarkOrderDeliveredOnFoodOrderDeliveredHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LA MOITIÉ DE CES MESSAGES NE NOUS CONCERNE PAS.
        if (!TicketDeLaMarketplace.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var result = await _sender.Send(
            new MarkOrderDeliveredCommand(integrationEvent.OrderId), cancellationToken);

        SagaOutcome.Exiger(
            result, _logger,
            "marquer la commande de repas livrée — SANS ELLE, L'ESCROW RESTE BLOQUÉ ET LE "
            + "RESTAURATEUR N'EST PAS RÉGLÉ",
            integrationEvent.OrderId, integrationEvent.FoodOrderId, integrationEvent.RestaurantId);
    }
}
