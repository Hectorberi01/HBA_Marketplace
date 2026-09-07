using System.Text.Json;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Webhooks;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>CE QUI MET LES WEBHOOKS EN FILE.</summary>
public sealed class DeliveryWebhookEnqueuer
{
    /// <summary>
    /// Sérialisation en camelCase : c'est ce qu'attend un intégrateur web, et le
    /// format est un CONTRAT EXTERNE. Le figer ici plutôt que de dépendre des
    /// réglages globaux de l'hôte évite qu'un changement de configuration de l'API
    /// modifie silencieusement la forme des webhooks déjà intégrés par des tiers.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDeliveryRepository _deliveries;
    private readonly IWebhookDeliveryRepository _webhooks;
    private readonly IDeliveryUnitOfWork _unitOfWork;
    private readonly ILogger<DeliveryWebhookEnqueuer> _logger;

    public DeliveryWebhookEnqueuer(
        IDeliveryRepository deliveries,
        IWebhookDeliveryRepository webhooks,
        IDeliveryUnitOfWork unitOfWork,
        ILogger<DeliveryWebhookEnqueuer> logger)
    {
        _deliveries = deliveries;
        _webhooks = webhooks;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task EnqueueAsync<TEvent>(
        TEvent integrationEvent, Guid deliveryId, string source, string eventType, CancellationToken ct)
        where TEvent : IntegrationEvent
    {
        if (!string.Equals(source, nameof(DeliverySource.ExternalPartner), StringComparison.Ordinal))
        {
            return;
        }

        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(deliveryId), ct);
        if (delivery?.PartnerId is not { } partnerId)
        {
            // Une course externe SANS partenaire est un invariant rompu — l'agrégat
            // le refuse à la création.
            _logger.LogWarning(
                "Webhook non mis en file pour la course {DeliveryId} ({EventType}) : aucun partenaire rattaché.",
                deliveryId, eventType);

            return;
        }

        // Le corps est sérialisé UNE FOIS et figé : c'est exactement cet octet-là
        // qui sera signé, et un corps re-sérialisé à chaque tentative produirait
        // une signature différente pour le même événement.
        var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), PayloadFormat);

        var webhook = WebhookDelivery.Enqueue(partnerId, integrationEvent.Id, eventType, payload);
        if (webhook.IsFailure)
        {
            _logger.LogError(
                "Webhook non mis en file pour la course {DeliveryId} : {Code}.",
                deliveryId, webhook.Error.Code);

            return;
        }

        await _webhooks.AddAsync(webhook.Value, ct);

        // AUCUN WEBHOOK N'A JAMAIS ETE ENREGISTRE, ET RIEN NE LE DISAIT.
        await _unitOfWork.SaveChangesAsync(ct);
    }
}

// UN HANDLER PAR ÉVÉNEMENT.

// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Deliveries.Application.Webhooks.WebhookOnDeliveryCreated")]
public sealed class WebhookOnDeliveryCreated : IIntegrationEventHandler<DeliveryCreatedIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryCreated(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryCreatedIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.created", ct);
}

[NomDeConsommateur("HBA.Deliveries.Application.Webhooks.WebhookOnDeliveryAccepted")]
public sealed class WebhookOnDeliveryAccepted : IIntegrationEventHandler<DeliveryAcceptedIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryAccepted(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryAcceptedIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.accepted", ct);
}

[NomDeConsommateur("HBA.Deliveries.Application.Webhooks.WebhookOnDeliveryPickedUp")]
public sealed class WebhookOnDeliveryPickedUp : IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryPickedUp(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryPickedUpIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.picked_up", ct);
}

[NomDeConsommateur("HBA.Deliveries.Application.Webhooks.WebhookOnDeliveryCompleted")]
public sealed class WebhookOnDeliveryCompleted : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryCompleted(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryCompletedIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.completed", ct);
}

[NomDeConsommateur("HBA.Deliveries.Application.Webhooks.WebhookOnDeliveryCancelled")]
public sealed class WebhookOnDeliveryCancelled : IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryCancelled(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryCancelledIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.cancelled", ct);
}

[NomDeConsommateur("HBA.Deliveries.Application.Webhooks.WebhookOnDeliveryNoDriver")]
public sealed class WebhookOnDeliveryNoDriver : IIntegrationEventHandler<DeliveryNoDriverAvailableIntegrationEvent>
{
    private readonly DeliveryWebhookEnqueuer _enqueuer;

    public WebhookOnDeliveryNoDriver(DeliveryWebhookEnqueuer enqueuer) => _enqueuer = enqueuer;

    public Task HandleAsync(DeliveryNoDriverAvailableIntegrationEvent e, CancellationToken ct = default)
        => _enqueuer.EnqueueAsync(e, e.DeliveryId, e.Source, "delivery.no_driver_available", ct);
}
