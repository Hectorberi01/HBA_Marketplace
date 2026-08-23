using HBA.Media.Contracts.IntegrationEvents;
using HBA.Media.Domain.Assets.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Media.Application.Assets.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CHAÎNON QUI MANQUAIT ENTRE LE DOMAINE ET KAFKA.
///
/// `MediaAsset` levait ses trois événements de domaine depuis l'origine, et la
/// documentation de ces événements affirmait qu'ils partaient par l'outbox. Il
/// n'existait aucun gestionnaire pour les traduire : ils étaient levés,
/// dispatchés dans le processus, et s'arrêtaient là.
///
/// CETTE INSCRIPTION EST MANUELLE, DONC OUBLIABLE.
///
/// Ces trois classes ne servent à rien tant que `MediaModuleInstaller` ne les
/// enregistre pas — et rien dans le compilateur ne le rappelle. C'est exactement
/// ainsi que payment-service a perdu `PaymentInitiatedDomainEventHandler` : la
/// classe existait, le service compilait, l'événement ne partait pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

/// <summary>
/// Publie « traitement échoué ».
///
/// L'ORIGINAL RESTE SERVABLE — voir l'encadré de
/// <see cref="MediaProcessingFailedIntegrationEvent"/>. Ce que cet événement
/// annonce, c'est l'absence de miniatures, pas la perte du fichier.
/// </summary>
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
