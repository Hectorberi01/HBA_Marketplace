using HBA.Delivery.Driver.Domain.Enums;
using HBA.Shared.Domain.Events;

namespace HBA.Delivery.Driver.Domain.Events;

/// <summary>LES TROIS FAITS DU DOSSIER LIVREUR.</summary>
public sealed record DriverAccountRegisteredDomainEvent(Guid DriverId, Guid UserId) : DomainEvent;

/// <summary>L'exploitation a vérifié un dossier.</summary>
public sealed record DriverAccountVerifiedDomainEvent(
    Guid DriverId,
    Guid UserId,
    string FullName,
    string Phone,
    DriverVehicleType Vehicle) : DomainEvent;

public sealed record DriverAccountSuspendedDomainEvent(Guid DriverId, Guid UserId, string? Reason) : DomainEvent;

/// <summary>Un véhicule vient d'être déclaré ou remplacé.</summary>
public sealed record DriverVehicleDeclaredDomainEvent(
    Guid DriverId, Guid VehicleId, DriverVehicleType Vehicle) : DomainEvent;
