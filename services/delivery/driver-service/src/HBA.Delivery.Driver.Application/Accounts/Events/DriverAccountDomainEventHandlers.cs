using HBA.Delivery.Driver.Domain.Events;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Drivers.Application.Accounts.Events;

// C'EST ICI QUE ISSUE-007 SE REFERME, ET IL FAUT COMPRENDRE OÙ ELLE ÉTAIT.

public sealed class DriverRegisteredDomainEventHandler
    : IDomainEventHandler<DriverAccountRegisteredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DriverRegisteredDomainEventHandler(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task HandleAsync(
        DriverAccountRegisteredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DriverCreatedIntegrationEvent
            {
                DriverId = domainEvent.DriverId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>La vérification publie DEUX événements, et ce n'est pas une redondance.</summary>
public sealed class DriverVerifiedDomainEventHandler
    : IDomainEventHandler<DriverAccountVerifiedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DriverVerifiedDomainEventHandler(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task HandleAsync(
        DriverAccountVerifiedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await _publisher.PublishAsync(
            new DriverVerifiedIntegrationEvent
            {
                DriverId = domainEvent.DriverId,
                UserId = domainEvent.UserId
            },
            cancellationToken);

        await _publisher.PublishAsync(
            new DriverDossierVerifiedIntegrationEvent
            {
                DriverId = domainEvent.DriverId,
                UserId = domainEvent.UserId,
                FullName = domainEvent.FullName,
                Phone = domainEvent.Phone,
                VehicleType = domainEvent.Vehicle.ToString()
            },
            cancellationToken);
    }
}

public sealed class DriverSuspendedDomainEventHandler
    : IDomainEventHandler<DriverAccountSuspendedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DriverSuspendedDomainEventHandler(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task HandleAsync(
        DriverAccountSuspendedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DriverSuspendedIntegrationEvent
            {
                DriverId = domainEvent.DriverId,

                // Le contrat exige un motif.
                Reason = domainEvent.Reason ?? "Non précisé."
            },
            cancellationToken);
}

/// <summary>L'UN DES DEUX ÉVÉNEMENTS QUE CE SERVICE PERDAIT (ISSUE-007).</summary>
public sealed class DriverVehicleDeclaredDomainEventHandler
    : IDomainEventHandler<DriverVehicleDeclaredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DriverVehicleDeclaredDomainEventHandler(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task HandleAsync(
        DriverVehicleDeclaredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DriverVehicleUpdatedIntegrationEvent
            {
                DriverId = domainEvent.DriverId,
                VehicleId = domainEvent.VehicleId,
                VehicleType = domainEvent.Vehicle.ToString()
            },
            cancellationToken);
}
