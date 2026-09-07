using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Application.Orders;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.Food.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le retour de course vers le ticket de cuisine : enlèvement, puis remise.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Food.Api.Integration.MarkFoodOrderPickedUpOnDeliveryPickedUpHandler")]
public sealed class MarkFoodOrderPickedUpOnDeliveryPickedUpHandler
    : IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkFoodOrderPickedUpOnDeliveryPickedUpHandler> _logger;

    public MarkFoodOrderPickedUpOnDeliveryPickedUpHandler(
        ISender sender, ILogger<MarkFoodOrderPickedUpOnDeliveryPickedUpHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryPickedUpIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (FoodOrderReference.Read(integrationEvent.Reference) is not { } foodOrderId)
        {
            // Commande marketplace, expédition, ou partenaire externe.
            return;
        }

        var resultat = await _sender.Send(
            new MarkFoodOrderPickedUpCommand(foodOrderId), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "marquer le repas enlevé — SANS ELLE, LA REMISE SERA REFUSÉE ET LE RESTAURATEUR "
            + "NE SERA PAS PAYÉ",
            foodOrderId, integrationEvent.DeliveryId);
    }
}

/// <summary>
/// Étape finale du parcours restauration : la course est terminée, le ticket passe
/// « livré » — et c'est ce qui déclenche le règlement du restaurateur.
/// </summary>
[NomDeConsommateur("HBA.Food.Api.Integration.MarkFoodOrderDeliveredOnDeliveryCompletedHandler")]
public sealed class MarkFoodOrderDeliveredOnDeliveryCompletedHandler
    : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkFoodOrderDeliveredOnDeliveryCompletedHandler> _logger;

    public MarkFoodOrderDeliveredOnDeliveryCompletedHandler(
        ISender sender, ILogger<MarkFoodOrderDeliveredOnDeliveryCompletedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (FoodOrderReference.Read(integrationEvent.Reference) is not { } foodOrderId)
        {
            return;
        }

        var resultat = await _sender.Send(
            new MarkFoodOrderDeliveredCommand(foodOrderId), cancellationToken);

        if (resultat.IsFailure && resultat.Error.Code == "food.order.not_picked_up")
        {
            _logger.LogWarning(
                "Course {DeliveryId} terminée alors que le ticket {FoodOrderId} n'a jamais été "
                + "marqué enlevé : l'enlèvement a été perdu. On le pose maintenant — une course "
                + "terminée prouve que le sac a été chargé.",
                integrationEvent.DeliveryId, foodOrderId);

            var enlevement = await _sender.Send(
                new MarkFoodOrderPickedUpCommand(foodOrderId), cancellationToken);

            SagaOutcome.Exiger(
                enlevement, _logger,
                "rattraper l'enlèvement manquant avant de marquer le repas livré",
                foodOrderId, integrationEvent.DeliveryId);

            resultat = await _sender.Send(
                new MarkFoodOrderDeliveredCommand(foodOrderId), cancellationToken);
        }

        // ON N'ÉCRIT PAS `=> _sender.Send(...)` ICI.
        SagaOutcome.Exiger(
            resultat, _logger,
            "marquer le repas livré — SANS ELLE, LA COMMANDE NE SE CLÔT PAS, L'ESCROW RESTE "
            + "BLOQUÉ ET LE RESTAURATEUR N'EST PAS PAYÉ",
            foodOrderId, integrationEvent.DeliveryId);
    }
}
