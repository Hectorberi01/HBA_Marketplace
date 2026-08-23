using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Abstractions;

public sealed record DeliveryPricingQuoteValidation(
    Guid QuoteId,
    bool Valid,
    string Status,
    decimal? Total,
    string? Currency);

public interface IDeliveryPricingQuoteValidator
{
    Task<Result<DeliveryPricingQuoteValidation>> ConsumeQuoteAsync(
        string quoteId,
        Guid deliveryId,
        CancellationToken cancellationToken = default);
}
