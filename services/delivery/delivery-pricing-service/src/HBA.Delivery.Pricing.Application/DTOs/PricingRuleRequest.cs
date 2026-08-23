namespace HBA.Delivery.Pricing.Application.DTOs;

public sealed record PricingRuleRequest(
    string Name,
    string Scope,
    long BaseFee,
    long PerKmFee,
    long PerMinuteFee,
    long MinFee,
    long? MaxFee,
    DateTimeOffset ActiveFrom,
    DateTimeOffset? ActiveTo,
    int Priority,
    string? ServiceLevel = "STANDARD",
    string? VehicleType = null,
    decimal? SurgeMultiplier = 1m);
