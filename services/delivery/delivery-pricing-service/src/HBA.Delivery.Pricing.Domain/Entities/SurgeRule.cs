namespace HBA.Delivery.Pricing.Domain.Entities;

public sealed record SurgeRule(Guid Id, string Name, decimal Multiplier, DateTimeOffset ActiveFrom, DateTimeOffset? ActiveTo, bool Active);
