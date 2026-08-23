using HBA.Delivery.Pricing.Domain.Entities;
using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Domain.Policies;

public static class PricingPolicy
{
    public static PriceBreakdown BuildBreakdown(PricingRule rule, int distanceMeters, int durationSeconds, long discount)
    {
        var distanceFee = (long)Math.Ceiling(distanceMeters / 1000m * rule.PerKmFee);
        var minuteFee = (long)Math.Ceiling(durationSeconds / 60m * rule.PerMinuteFee);
        var baseSubtotal = rule.BaseFee + distanceFee + minuteFee;
        var surgedSubtotal = (long)Math.Round(baseSubtotal * rule.SurgeMultiplier, MidpointRounding.AwayFromZero);
        var surgeFee = Math.Max(0, surgedSubtotal - baseSubtotal);

        return new PriceBreakdown(rule.BaseFee, distanceFee, minuteFee, surgeFee, discount);
    }

    public static long CalculateSubtotal(PricingRule rule, PriceBreakdown breakdown)
    {
        var surgedSubtotal = rule.BaseFee + breakdown.DistanceFee + breakdown.MinuteFee + breakdown.SurgeFee;
        return Math.Clamp(surgedSubtotal, rule.MinFee, rule.MaxFee ?? long.MaxValue);
    }

    public static long CalculateTotal(long subtotal, long discount) => Math.Max(0, subtotal - discount);
}
