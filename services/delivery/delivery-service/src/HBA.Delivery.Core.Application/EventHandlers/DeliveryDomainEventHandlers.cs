using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Deliveries.Domain.Deliveries.Events;
using HBA.Deliveries.Domain.Drivers.Events;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Deliveries.Application.Deliveries.EventHandlers;

/// <summary>TRADUCTION DES FAITS INTERNES EN FAITS PUBLICS.</summary>
public sealed class DeliveryCreatedDomainEventHandler : IDomainEventHandler<DeliveryCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCreatedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCreatedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Type = domainEvent.Type.ToString()
            },
            cancellationToken);
}

/// <summary>Une course vient d'être proposée à un livreur.</summary>
public sealed class DeliveryAssignedDomainEventHandler : IDomainEventHandler<DeliveryAssignedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryAssignedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryAssignedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryAssignedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                DriverId = domainEvent.DriverId
            },
            cancellationToken);
}

/// <summary>
/// L'exploitation a vérifié les pièces d'un livreur : il est autorisé à travailler.
/// </summary>
public sealed class DriverVerifiedDomainEventHandler : IDomainEventHandler<DriverVerifiedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DriverVerifiedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DriverVerifiedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DriverVerifiedIntegrationEvent
            {
                DriverId = domainEvent.DriverId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Un livreur a accepté : le client peut être prévenu.</summary>
public sealed class DeliveryAcceptedDomainEventHandler : IDomainEventHandler<DeliveryAcceptedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryAcceptedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryAcceptedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryAcceptedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId
            },
            cancellationToken);
}

/// <summary>Colis pris en charge : la course est physiquement engagée.</summary>
public sealed class DeliveryPickedUpDomainEventHandler : IDomainEventHandler<DeliveryPickedUpDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ISecretProtector _protecteur;

    public DeliveryPickedUpDomainEventHandler(
        IIntegrationEventPublisher publisher, ISecretProtector protecteur)
    {
        _publisher = publisher;
        _protecteur = protecteur;
    }

    public Task HandleAsync(DeliveryPickedUpDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryPickedUpIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId,

                // NUL quand la course ne demande pas de code — photo, signature, ou
                // aucune preuve.
                ProtectedDeliveryPin = domainEvent.IssuedPin is { Length: > 0 } code
                    ? _protecteur.Protect(code)
                    : null
            },
            cancellationToken);
}

/// <summary>
/// Remise effectuée — l'événement le plus consommé du module : il clôt la commande,
/// déclenche le gain du livreur et part en webhook partenaire.
/// </summary>
public sealed class DeliveryCompletedDomainEventHandler : IDomainEventHandler<DeliveryCompletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCompletedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCompletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCompletedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId,
                DeliveredAtUtc = domainEvent.DeliveredAtUtc,
                DriverEarning = domainEvent.DriverEarning,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>Annulation avant collecte.</summary>
public sealed class DeliveryCancelledDomainEventHandler : IDomainEventHandler<DeliveryCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCancelledIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Aucun livreur trouvé : appel au dispatch humain, pas au client final.</summary>
public sealed class DeliveryNoDriverAvailableDomainEventHandler
    : IDomainEventHandler<DeliveryNoDriverAvailableDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryNoDriverAvailableDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        DeliveryNoDriverAvailableDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryNoDriverAvailableIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Attempts = domainEvent.Attempts
            },
            cancellationToken);
}
