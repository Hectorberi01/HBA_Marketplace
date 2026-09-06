using HBA.Shared.IntegrationEvents;

namespace HBA.Drivers.Contracts.IntegrationEvents;

[HbaEvent("driver.created", Version = 1, AggregateType = "Driver")]
public sealed record DriverCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required Guid UserId { get; init; }
}

[HbaEvent("driver.verified", Version = 1, AggregateType = "Driver")]
public sealed record DriverVerifiedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required Guid UserId { get; init; }
}

[HbaEvent("driver.suspended", Version = 1, AggregateType = "Driver")]
public sealed record DriverSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required string Reason { get; init; }
}

[HbaEvent("driver.availability-changed", Version = 1, AggregateType = "Driver")]
public sealed record DriverAvailabilityChangedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required string Availability { get; init; }
}

[HbaEvent("driver.vehicle-updated", Version = 1, AggregateType = "Driver")]
public sealed record DriverVehicleUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required Guid VehicleId { get; init; }
    public required string VehicleType { get; init; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DOSSIER D'UN LIVREUR VIENT D'ÊTRE VÉRIFIÉ — AVEC DE QUOI LE PROJETER.
///
/// POURQUOI UN SECOND ÉVÉNEMENT À CÔTÉ DE `DriverVerifiedIntegrationEvent`.
///
/// L'ancien ne porte que deux identifiants, et son consommateur utile est
/// identity-service, qui attribue le rôle « Driver » : il n'a besoin de rien de
/// plus. Celui-ci s'adresse à delivery-service, qui doit CRÉER la ligne
/// `deliveries.drivers` — donc appeler `Driver.Register(userId, fullName, phone,
/// vehicle)`. Sans ces trois champs, le consommateur devrait rappeler
/// driver-service par gRPC juste après l'avoir écouté, et l'échec de cet appel
/// laisserait un livreur vérifié que le dispatch ignore, sans rien pour le
/// signaler.
///
/// NE PAS AVOIR ÉTENDU `DriverVerifiedIntegrationEvent` EST DÉLIBÉRÉ. Ajouter
/// des champs `required` à un contrat déjà publié casse la désérialisation des
/// messages en vol et de tout ce qui dort dans une outbox. Un nouveau nom ne
/// casse rien : les anciens consommateurs continuent d'ignorer ce qu'ils ne
/// connaissent pas.
///
/// CE QUE CET ÉVÉNEMENT NE COUVRE PAS : LA SUSPENSION. `DriverSuspendedIntegrationEvent`
/// existe et est publié, mais RIEN NE LE CONSOMME aujourd'hui. Un livreur suspendu
/// dans son dossier continue donc de recevoir des propositions tant que
/// delivery-service ne le lit pas. C'est le manque le plus sérieux que ce
/// découpage laisse ouvert.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[HbaEvent("driver.dossier-verified", Version = 1, AggregateType = "Driver")]
public sealed record DriverDossierVerifiedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required Guid UserId { get; init; }
    public required string FullName { get; init; }
    public required string Phone { get; init; }

    /// <summary>Nom de l'énumération, pas son entier : « Motorcycle », « Tricycle »…</summary>
    public required string VehicleType { get; init; }
}
