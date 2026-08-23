namespace HBA.Delivery.Pricing.Application.Commands.ConsumeQuote;

public sealed record ConsumeQuoteCommand(Guid QuoteId, Guid DeliveryId);
