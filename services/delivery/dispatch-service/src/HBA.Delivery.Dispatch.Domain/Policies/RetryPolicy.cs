namespace HBA.Delivery.Dispatch.Domain.Policies;

public static class RetryPolicy
{
    public static bool CanRetry(int attempt, int maxAttempts = 4) => attempt < maxAttempts;

    public static int NextSearchRadiusMeters(int currentRadiusMeters) =>
        Math.Clamp(currentRadiusMeters + 1500, 1500, 15000);
}
