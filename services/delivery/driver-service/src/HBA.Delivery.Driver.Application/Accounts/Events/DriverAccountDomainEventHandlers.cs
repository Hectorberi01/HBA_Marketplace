using HBA.Delivery.Driver.Domain.Events;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Drivers.Application.Accounts.Events;

// ═════════════════════════════════════════════════════════════════════════════
// C'EST ICI QUE ISSUE-007 SE REFERME, ET IL FAUT COMPRENDRE OÙ ELLE ÉTAIT.
//
// Avant ce lot, `DriverStore` appelait `IIntegrationEventPublisher.PublishAsync`
// directement depuis une méthode qui n'écrivait rien en base. Le publieur enregistré
// était `IntegrationEventQueue` — une `List<>` scopée que le `DbContext` du module
// est censé drainer. Il n'y avait pas de `DbContext`. La liste mourait avec la
// requête, `PublishAsync` rendait `Task.CompletedTask`, et l'appelant voyait un
// succès. LA PERTE ÉTAIT TOTALE ET SYSTÉMATIQUE, pas occasionnelle.
//
// Le chemin est maintenant celui des vingt-deux autres modules :
//
//   agrégat lève un ÉVÉNEMENT DE DOMAINE
//     → `ModuleDbContext.SaveChangesAsync` le dispatche
//       → ce gestionnaire met un ÉVÉNEMENT D'INTÉGRATION dans la file
//         → le MÊME `SaveChanges` draine la file vers `drivers.outbox_messages`
//           → `AddOutboxProcessor<DriverDbContext>()` publie sur Kafka
//
// L'effet métier et le message partent donc dans la même transaction : ou les deux,
// ou aucun. C'est ce que `DriversModuleInstaller` enregistre, et sans ces quatre
// `AddScoped` le dispatcheur ne trouverait aucun gestionnaire — ce qui n'est PAS
// une erreur de démarrage, c'est un silence.
// ═════════════════════════════════════════════════════════════════════════════

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

/// <summary>
/// La vérification publie DEUX événements, et ce n'est pas une redondance.
///
/// `DriverVerifiedIntegrationEvent` est le fait nu, destiné à identity-service qui
/// attribue le rôle « Driver ». `DriverDossierVerifiedIntegrationEvent` porte
/// l'identité complète, destinée à delivery-service qui doit créer la projection
/// dispatchable. Les fusionner obligerait identity-service à recevoir le nom, le
/// téléphone et le véhicule d'un livreur pour attribuer un rôle — voir l'encadré
/// du contrat.
/// </summary>
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

                // Le contrat exige un motif. Une suspension sans motif saisi reste
                // une suspension : mieux vaut un libellé neutre qu'un message qui
                // n'est jamais publié parce qu'une propriété `required` est nulle.
                Reason = domainEvent.Reason ?? "Non précisé."
            },
            cancellationToken);
}

/// <summary>
/// L'UN DES DEUX ÉVÉNEMENTS QUE CE SERVICE PERDAIT (ISSUE-007). Il part
/// désormais par l'outbox, dans la transaction qui écrit le véhicule.
/// </summary>
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
