using HBA.Delivery.Pricing.Application.DTOs;

namespace HBA.Delivery.Pricing.Application.Commands.UpdatePricingRule;

public sealed record UpdatePricingRuleCommand(Guid PricingRuleId, PricingRuleRequest Request);
