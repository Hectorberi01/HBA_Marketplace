namespace HBA.Delivery.Pricing.Domain.Entities;

public sealed record PricingRule(
    Guid Id,
    string Name,
    string Scope,
    string ServiceLevel,
    string? VehicleType,
    long BaseFee,
    long PerKmFee,
    long PerMinuteFee,
    long MinFee,
    long? MaxFee,
    DateTimeOffset ActiveFrom,
    DateTimeOffset? ActiveTo,
    int Priority,
    decimal SurgeMultiplier,
    string Status);
