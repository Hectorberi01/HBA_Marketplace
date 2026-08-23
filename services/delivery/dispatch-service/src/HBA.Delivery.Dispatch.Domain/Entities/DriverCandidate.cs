namespace HBA.Delivery.Dispatch.Domain.Entities;

public sealed record DriverCandidate(
    Guid DispatchJobId,
    Guid DriverId,
    int DistanceToPickupMeters,
    int EtaSeconds,
    decimal Score,
    int Rank,
    DateTimeOffset EvaluatedAt);
