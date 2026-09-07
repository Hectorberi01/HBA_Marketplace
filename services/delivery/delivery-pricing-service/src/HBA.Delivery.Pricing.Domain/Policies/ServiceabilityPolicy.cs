using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Domain.Policies;

/// <summary>LA DISTANCE EST UNE LIGNE DROITE, ET LE NOM DE LA MÉTHODE LE DIT.</summary>
public static class ServiceabilityPolicy
{
    public const int MaximumDistanceMeters = 25_000;

    public static bool IsServiceable(int distanceMeters) => distanceMeters <= MaximumDistanceMeters;

    /// <summary>
    /// Distance à vol d'oiseau, corrigée par
    /// <paramref name="facteurCorrectionUrbaine"/> .
    /// </summary>
    public static int DistanceRoutiereEstimeeMetres(
        GeoPoint pickup, GeoPoint dropoff, decimal facteurCorrectionUrbaine)
        => (int)Math.Round(HaversineMeters(pickup, dropoff) * facteurCorrectionUrbaine,
                           MidpointRounding.AwayFromZero);

    public static int HaversineMeters(GeoPoint pickup, GeoPoint dropoff)
    {
        const double earthRadius = 6_371_000;
        var dLat = DegreesToRadians(dropoff.Latitude - pickup.Latitude);
        var dLon = DegreesToRadians(dropoff.Longitude - pickup.Longitude);
        var lat1 = DegreesToRadians(pickup.Latitude);
        var lat2 = DegreesToRadians(dropoff.Latitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return (int)Math.Round(earthRadius * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h)));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;
}
