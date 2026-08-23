namespace HBA.Delivery.Dispatch.Domain.Policies;

public static class OfferStrategyPolicy
{
    public static int MaxParallelOffers(int priority) => priority >= 8 ? 5 : 3;

    public static DateTimeOffset OfferExpiresAt(DateTimeOffset now) => now.AddSeconds(45);
}
