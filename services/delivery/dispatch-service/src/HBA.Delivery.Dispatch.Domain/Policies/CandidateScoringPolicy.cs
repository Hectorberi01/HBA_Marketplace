namespace HBA.Delivery.Dispatch.Domain.Policies;

public static class CandidateScoringPolicy
{
    public static decimal Score(int distanceToPickupMeters, int etaSeconds, decimal driverRating)
    {
        var distanceScore = Math.Max(0m, 1m - distanceToPickupMeters / 15000m);
        var etaScore = Math.Max(0m, 1m - etaSeconds / 1800m);
        var ratingScore = Math.Clamp(driverRating / 5m, 0m, 1m);
        return Math.Round(distanceScore * 0.55m + etaScore * 0.30m + ratingScore * 0.15m, 4);
    }
}
