using HBA.Shared.IntegrationEvents;

namespace HBA.DeliveryPricing.Contracts.IntegrationEvents;

[HbaEvent("delivery.pricing.quote-created", Version = 1, AggregateType = "DeliveryQuote")]
public sealed record DeliveryQuoteCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid QuoteId { get; init; }
    public required long Total { get; init; }
    public required string Currency { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}

[HbaEvent("delivery.pricing.quote-expired", Version = 1, AggregateType = "DeliveryQuote")]
public sealed record DeliveryQuoteExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid QuoteId { get; init; }
}

[HbaEvent("delivery.pricing.quote-consumed", Version = 1, AggregateType = "DeliveryQuote")]
public sealed record DeliveryQuoteConsumedIntegrationEvent : IntegrationEvent
{
    public required Guid QuoteId { get; init; }
    public required Guid DeliveryId { get; init; }
}

[HbaEvent("delivery.pricing.rule-created", Version = 1, AggregateType = "PricingRule")]
public sealed record DeliveryPricingRuleCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid PricingRuleId { get; init; }
    public required string Name { get; init; }
}

[HbaEvent("delivery.pricing.rule-updated", Version = 1, AggregateType = "PricingRule")]
public sealed record DeliveryPricingRuleUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid PricingRuleId { get; init; }
    public required string Name { get; init; }
}

[HbaEvent("delivery.pricing.rule-activated", Version = 1, AggregateType = "PricingRule")]
public sealed record DeliveryPricingRuleActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid PricingRuleId { get; init; }
}

[HbaEvent("delivery.pricing.rule-deactivated", Version = 1, AggregateType = "PricingRule")]
public sealed record DeliveryPricingRuleDeactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid PricingRuleId { get; init; }
}
