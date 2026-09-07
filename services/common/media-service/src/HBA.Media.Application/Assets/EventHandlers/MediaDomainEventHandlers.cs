using HBA.Media.Contracts.IntegrationEvents;
using HBA.Media.Domain.Assets.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Media.Application.Assets.EventHandlers;

/// <summary>LE CHAÎNON QUI MANQUAIT ENTRE LE DOMAINE ET KAFKA.</summary>
public sealed class MediaReadyDomainEventHandler : IDomainEventHandler<MediaReadyDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MediaReadyDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(MediaReadyDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MediaReadyIntegrationEvent
            {
                MediaId = domainEvent.MediaId,
                OwnerType = domainEvent.OwnerType,
                OwnerId = domainEvent.OwnerId,
                MediaType = domainEvent.MediaType,
                ObjectKey = domainEvent.ObjectKey
            },
            cancellationToken);
}

/// <summary>
/// Publie « média supprimé » : le service propriétaire peut oublier la référence.
/// </summary>
public sealed class MediaDeletedDomainEventHandler : IDomainEventHandler<MediaDeletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MediaDeletedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(MediaDeletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MediaDeletedIntegrationEvent
            {
                MediaId = domainEvent.MediaId,
                OwnerType = domainEvent.OwnerType,
                OwnerId = domainEvent.OwnerId,
                MediaType = domainEvent.MediaType
            },
            cancellationToken);
}

/// <summary>Publie « traitement échoué ».</summary>
public sealed class MediaProcessingFailedDomainEventHandler
    : IDomainEventHandler<MediaProcessingFailedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MediaProcessingFailedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        MediaProcessingFailedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MediaProcessingFailedIntegrationEvent
            {
                MediaId = domainEvent.MediaId,
                OwnerType = domainEvent.OwnerType,
                OwnerId = domainEvent.OwnerId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}
