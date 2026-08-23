namespace HBA.Delivery.Pricing.Domain.ValueObjects;

public sealed record PriceBreakdown(long BaseFee, long DistanceFee, long MinuteFee, long SurgeFee, long Discount);
