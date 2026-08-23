namespace HBA.Tracking.Domain.Tracking;

public sealed record TrackingSession(
    Guid Id,
    Guid DeliveryId,
    Guid DriverId,
    TrackingSessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long LastSequence);

public sealed record LocationPoint(
    long Sequence,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    double? SpeedMps,
    double? Heading,
    DateTimeOffset CapturedAt);

public sealed record TrackingSnapshot(
    Guid DeliveryId,
    Guid DriverId,
    double Latitude,
    double Longitude,
    DateTimeOffset CapturedAt,
    int? EtaSeconds,
    RouteProgress? RouteProgress);

public sealed record RouteProgress(decimal Ratio, int RemainingMeters);

public enum TrackingSessionStatus { Active, Completed, Cancelled }

public static class TrackingQualityPolicy
{
    public static bool IsPlausible(LocationPoint point, DateTimeOffset now) =>
        point.AccuracyMeters is null or <= 150
        && point.SpeedMps is null or <= 45
        && point.CapturedAt <= now.AddMinutes(2);
}
