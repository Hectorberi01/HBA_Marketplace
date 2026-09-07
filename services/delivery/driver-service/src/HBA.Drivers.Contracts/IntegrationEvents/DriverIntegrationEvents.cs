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

/// <summary>LE DOSSIER D'UN LIVREUR VIENT D'ÊTRE VÉRIFIÉ — AVEC DE QUOI LE PROJETER.</summary>
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
