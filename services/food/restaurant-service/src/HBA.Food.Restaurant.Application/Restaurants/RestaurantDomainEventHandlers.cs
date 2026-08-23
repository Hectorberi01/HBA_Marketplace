using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Domain.Restaurants.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Food.Application.Restaurants;

/// <summary>
/// Publie « établissement validé ».
///
/// SANS CE PUBLICATEUR, RestaurantApprovedDomainEvent MOURAIT DANS L'AGRÉGAT.
///
/// L'événement était levé, correctement, et personne ne l'écoutait : le rôle
/// FoodPartner n'était jamais attribué. Le jour où une route l'aurait exigé,
/// aucun restaurateur validé ne l'aurait eu — et personne n'aurait relié la panne
/// à un événement sans écouteur, écrit des mois plus tôt.
/// </summary>
public sealed class RestaurantApprovedDomainEventHandler : IDomainEventHandler<RestaurantApprovedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RestaurantApprovedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(RestaurantApprovedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new RestaurantApprovedIntegrationEvent
            {
                RestaurantId = domainEvent.RestaurantId,
                OwnerUserId = domainEvent.OwnerUserId,
                Name = domainEvent.Name
            },
            cancellationToken);
}

/// <summary>
/// Publie « dossier refusé ». Le motif voyage avec — c'est tout l'intérêt.
/// </summary>
public sealed class RestaurantRejectedDomainEventHandler : IDomainEventHandler<RestaurantRejectedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RestaurantRejectedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(RestaurantRejectedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new RestaurantRejectedIntegrationEvent
            {
                RestaurantId = domainEvent.RestaurantId,
                OwnerUserId = domainEvent.OwnerUserId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie « établissement suspendu ».</summary>
public sealed class RestaurantSuspendedDomainEventHandler : IDomainEventHandler<RestaurantSuspendedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RestaurantSuspendedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(RestaurantSuspendedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new RestaurantSuspendedIntegrationEvent
            {
                RestaurantId = domainEvent.RestaurantId,
                OwnerUserId = domainEvent.OwnerUserId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie « suspension levée ».</summary>
public sealed class RestaurantReopenedDomainEventHandler : IDomainEventHandler<RestaurantReopenedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RestaurantReopenedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(RestaurantReopenedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new RestaurantReopenedIntegrationEvent
            {
                RestaurantId = domainEvent.RestaurantId,
                OwnerUserId = domainEvent.OwnerUserId
            },
            cancellationToken);
}
