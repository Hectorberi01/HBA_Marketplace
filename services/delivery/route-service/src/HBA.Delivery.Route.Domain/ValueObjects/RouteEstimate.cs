namespace HBA.Delivery.Route.Domain.ValueObjects;

public sealed record RouteEstimate(
    int DistanceMeters,
    int DurationSeconds,
    string Provider,
    string? EncodedPolyline);
