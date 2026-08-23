namespace HBA.Routes.Domain.Routes;

public sealed record GeoPoint(double Latitude, double Longitude);

public sealed record RoutePlan(
    Guid Id,
    Guid? DeliveryId,
    string Provider,
    GeoPoint Origin,
    GeoPoint Destination,
    IReadOnlyList<GeoPoint> Waypoints,
    int DistanceMeters,
    int DurationSeconds,
    string? EncodedPolyline,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset EtaAt);

public sealed record EtaSnapshot(
    Guid Id,
    Guid DeliveryId,
    Guid RoutePlanId,
    DateTimeOffset EtaAt,
    int RemainingMeters,
    DateTimeOffset ComputedAt,
    string Source);

public static class RouteMath
{
    public static int HaversineMeters(GeoPoint a, GeoPoint b)
    {
        const double earthRadius = 6371000;
        var dLat = DegreesToRadians(b.Latitude - a.Latitude);
        var dLon = DegreesToRadians(b.Longitude - a.Longitude);
        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return (int)Math.Round(earthRadius * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h)));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;
}
