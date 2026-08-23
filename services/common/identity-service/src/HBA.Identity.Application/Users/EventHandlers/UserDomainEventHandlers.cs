using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Identity.Domain.Users.Events;

namespace HBA.Identity.Application.Users.EventHandlers;

/// <summary>Publie l'event d'intégration « compte créé » (bienvenue, analytics…).</summary>
public sealed class UserRegisteredDomainEventHandler : IDomainEventHandler<UserRegisteredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public UserRegisteredDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new UserRegisteredIntegrationEvent
            {
                UserId = domainEvent.UserId,
                Email = domainEvent.Email,
                FirstName = domainEvent.FirstName
            },
            cancellationToken);
}

/// <summary>
/// Publie l'event d'intégration « nom modifié », qui tient le profil du module
/// User aligné. L'agrégat ne lève l'event que si le nom a réellement changé.
/// </summary>
public sealed class UserProfileUpdatedDomainEventHandler : IDomainEventHandler<UserProfileUpdatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public UserProfileUpdatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(UserProfileUpdatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new UserProfileUpdatedIntegrationEvent
            {
                UserId = domainEvent.UserId,
                FirstName = domainEvent.FirstName,
                LastName = domainEvent.LastName
            },
            cancellationToken);
}

/// <summary>
/// Publie l'event d'intégration « compte anonymisé ». C'est ce qui déclenche la
/// purge des données personnelles qui vivent HORS du schéma identity.
/// </summary>
public sealed class UserAnonymizedDomainEventHandler : IDomainEventHandler<UserAnonymizedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public UserAnonymizedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(UserAnonymizedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new UserAnonymizedIntegrationEvent { UserId = domainEvent.UserId },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « e-mail confirmé ».</summary>
public sealed class UserEmailConfirmedDomainEventHandler : IDomainEventHandler<UserEmailConfirmedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public UserEmailConfirmedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(UserEmailConfirmedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new UserEmailConfirmedIntegrationEvent
            {
                UserId = domainEvent.UserId,
                Email = domainEvent.Email
            },
            cancellationToken);
}
