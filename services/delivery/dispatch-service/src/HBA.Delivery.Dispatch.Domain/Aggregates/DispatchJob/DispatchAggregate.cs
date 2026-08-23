namespace HBA.Dispatch.Domain.Dispatching;

public sealed record DispatchJob(
    Guid Id,
    Guid DeliveryId,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    string? VehicleRequirement,
    int Priority,
    DispatchStatus Status,
    int Attempt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    int SearchRadiusMeters);

public sealed record DriverCandidate(
    Guid DispatchJobId,
    Guid DriverId,
    int DistanceToPickupMeters,
    int EtaSeconds,
    decimal Score,
    int Rank,
    DateTimeOffset EvaluatedAt);

public sealed record Assignment(
    Guid Id,
    Guid DeliveryId,
    Guid DriverId,
    Guid? OfferId,
    DateTimeOffset AssignedAt,
    string AssignmentMode,
    AssignmentStatus Status);

public sealed record GeoPoint(double Latitude, double Longitude);

public enum DispatchStatus { Pending, Offering, Assigned, Cancelled, NoDriverFound }
public enum AssignmentStatus { Assigned, Accepted, Cancelled }

public static class DispatchPolicy
{
    public static int NextSearchRadiusMeters(int currentRadiusMeters) =>
        Math.Clamp(currentRadiusMeters + 1500, 1500, 15000);

    public static DateTimeOffset OfferExpiresAt(DateTimeOffset now) => now.AddSeconds(45);
}
