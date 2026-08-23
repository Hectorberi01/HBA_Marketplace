namespace HBA.Delivery.Dispatch.Domain.Entities;

public sealed record Assignment(
    Guid Id,
    Guid DeliveryId,
    Guid DriverId,
    Guid? OfferId,
    DateTimeOffset AssignedAt,
    string AssignmentMode,
    string Status);
