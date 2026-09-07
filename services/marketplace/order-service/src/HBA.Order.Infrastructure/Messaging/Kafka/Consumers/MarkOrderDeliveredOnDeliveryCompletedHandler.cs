using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Orders.Application.Orders;
using HBA.Orders.Application.Orders.EventHandlers;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// La référence sous laquelle une commande marketplace se reconnaît dans une course
/// HBA Delivery.
/// </summary>
public static class OrderDeliveryReference
{
    // DÉLÉGUÉ AU SOCLE PARTAGÉ — voir `DeliveryReference`.
    public static string For(Guid orderId) => DeliveryReference.ForOrder(orderId);

    public static Guid? Read(string? reference) => DeliveryReference.ReadOrder(reference);
}

/// <summary>Étape finale du Saga : la course est terminée, la commande passe « livrée ».</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Orders.Application.Orders.EventHandlers.MarkOrderDeliveredOnDeliveryCompletedHandler")]
public sealed class MarkOrderDeliveredOnDeliveryCompletedHandler
    : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkOrderDeliveredOnDeliveryCompletedHandler> _logger;

    public MarkOrderDeliveredOnDeliveryCompletedHandler(
        ISender sender, ILogger<MarkOrderDeliveredOnDeliveryCompletedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (OrderDeliveryReference.Read(integrationEvent.Reference) is not { } orderId)
        {
            // Course restauration, expédition, ou partenaire externe.
            return;
        }

        var result = await _sender.Send(new MarkOrderDeliveredCommand(orderId), cancellationToken);

        // CE HANDLER JOURNALISAIT TOUT, SANS JAMAIS LEVER.
        SagaOutcome.Exiger(
            result, _logger,
            "marquer la commande livrée — SANS ELLE, L'ESCROW RESTE BLOQUÉ ET LE VENDEUR N'EST PAS RÉGLÉ",
            orderId, integrationEvent.DeliveryId);
    }
}
