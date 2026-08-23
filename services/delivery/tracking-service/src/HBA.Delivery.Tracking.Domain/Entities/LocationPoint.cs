namespace HBA.Delivery.Tracking.Domain.Entities;

public sealed record LocationPoint(
    Guid Id,
    Guid DeliveryId,
    Guid DriverId,
    long Sequence,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    double? SpeedMps,
    double? Heading,
    DateTimeOffset CapturedAt);
