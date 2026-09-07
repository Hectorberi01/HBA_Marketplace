using Grpc.Core;
using HBA.Deliveries.Application.Abstractions;
using HBA.DeliveryPricing.Grpc.V1;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Infrastructure.Pricing;

// `internal` DEPUIS LA DISSOLUTION DES ASSEMBLAGES DE CONTRATS (lot D).
internal sealed class GrpcDeliveryPricingQuoteValidator : IDeliveryPricingQuoteValidator
{
    private readonly DeliveryPricingApi.DeliveryPricingApiClient _client;

    public GrpcDeliveryPricingQuoteValidator(DeliveryPricingApi.DeliveryPricingApiClient client)
    {
        _client = client;
    }

    public async Task<Result<DeliveryPricingQuoteValidation>> ConsumeQuoteAsync(
        string quoteId,
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(quoteId, out var parsedQuoteId))
        {
            return Result.Failure<DeliveryPricingQuoteValidation>(
                Error.Validation("pricing.quote.malformed", "Référence de devis illisible."));
        }

        try
        {
            var response = await _client.ConsumeQuoteAsync(
                new ConsumeQuoteRequest
                {
                    QuoteId = parsedQuoteId.ToString(),
                    DeliveryId = deliveryId.ToString()
                },
                cancellationToken: cancellationToken);

            // ICI L'ARGENT CHANGE DE REPRÉSENTATION (D39).
            return new DeliveryPricingQuoteValidation(
                parsedQuoteId,
                response.Valid,
                response.Status,
                response.HasTotal ? response.Total : null,
                response.HasCurrency ? response.Currency : null);
        }
        // QUATRE STATUTS, ET NON PLUS DEUX — CE FILTRE ÉTAIT LE SEUL DU DÉPÔT ET IL
        // LAISSAIT PASSER LES DEUX PANNES LES PLUS PROBABLES.
        catch (RpcException exception) when (exception.StatusCode
            is StatusCode.Unavailable
            or StatusCode.DeadlineExceeded
            or StatusCode.Unauthenticated
            or StatusCode.FailedPrecondition)
        {
            return Result.Failure<DeliveryPricingQuoteValidation>(
                Error.DependencyUnavailable(
                    $"pricing.grpc_{exception.StatusCode.ToString().ToLowerInvariant()}",
                    "Delivery Pricing Service est indisponible."));
        }
    }
}
