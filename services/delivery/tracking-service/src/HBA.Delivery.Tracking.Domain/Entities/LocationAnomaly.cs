namespace HBA.Delivery.Tracking.Domain.Entities;

public sealed record LocationAnomaly(
    Guid Id,
    Guid DeliveryId,
    Guid DriverId,
    long Sequence,
    string Reason,
    DateTimeOffset DetectedAt);
