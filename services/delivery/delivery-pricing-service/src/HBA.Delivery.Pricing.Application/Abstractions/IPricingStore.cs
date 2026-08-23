using HBA.Delivery.Pricing.Application.DTOs;
using HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;
using HBA.Delivery.Pricing.Domain.Entities;
using HBA.Shared.IntegrationEvents;

namespace HBA.Delivery.Pricing.Application.Abstractions;

public interface IPricingStore
{
    Task<DeliveryQuote> CreateQuoteAsync(CreateQuoteRequest request, IIntegrationEventPublisher publisher, CancellationToken cancellationToken = default);
    Task<DeliveryQuote?> GetQuoteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<QuoteValidation> ValidateQuoteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<QuoteValidation> ConsumeQuoteAsync(Guid id, Guid deliveryId, IIntegrationEventPublisher publisher, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PricingRule>> ListRulesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryZone>> ListZonesAsync(CancellationToken cancellationToken = default);
    Task<PricingRule> AddRuleAsync(PricingRuleRequest request, IIntegrationEventPublisher publisher, CancellationToken cancellationToken = default);
    Task<PricingRule?> UpdateRuleAsync(Guid id, PricingRuleRequest request, IIntegrationEventPublisher publisher, CancellationToken cancellationToken = default);
    Task<PricingRule?> SetRuleStatusAsync(Guid id, bool active, IIntegrationEventPublisher publisher, CancellationToken cancellationToken = default);
    Task<Serviceability> GetServiceabilityAsync(ServiceabilityRequest request, CancellationToken cancellationToken = default);
}
