namespace HBA.Delivery.Pricing.Domain.Events;

public sealed record DeliveryQuoteConsumedDomainEvent(Guid QuoteId, Guid DeliveryId);
