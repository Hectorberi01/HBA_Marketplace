using System.Globalization;
using Grpc.Core;
using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Application.DTOs;
using HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;
using HBA.DeliveryPricing.Grpc.V1;
using HBA.Shared.IntegrationEvents;

namespace HBA.Delivery.Pricing.Api.GrpcServices;

public sealed class DeliveryPricingGrpcService : DeliveryPricingApi.DeliveryPricingApiBase
{
    private readonly IPricingStore _pricing;
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryPricingGrpcService(IPricingStore pricing, IIntegrationEventPublisher publisher)
    {
        _pricing = pricing;
        _publisher = publisher;
    }

    public override async Task<DeliveryQuoteReply> QuoteDelivery(QuoteDeliveryRequest request, ServerCallContext context)
    {
        var quote = await _pricing.CreateQuoteAsync(new CreateQuoteRequest(
            ParseOptionalGuid(request.HasSellerId, request.SellerId),
            ParseOptionalGuid(request.HasStoreId, request.StoreId),
            FromProto(request.Pickup),
            FromProto(request.Dropoff),
            request.HasDistanceMeters ? request.DistanceMeters : null,
            request.HasDurationSeconds ? request.DurationSeconds : null,
            request.HasVehicleType ? request.VehicleType : null,
            request.HasServiceLevel ? request.ServiceLevel : null,
            request.Discount,
            request.HasCurrency ? request.Currency : null),
            _publisher,
            context.CancellationToken);

        return ToProto(quote);
    }

    public override async Task<ValidateQuoteResponse> ConsumeQuote(
        ConsumeQuoteRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.QuoteId, out var quoteId) || !Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "quote_id ou delivery_id invalide."));
        }

        var validation = await _pricing.ConsumeQuoteAsync(quoteId, deliveryId, _publisher, context.CancellationToken);
        return ToValidationProto(validation);
    }

    public override async Task<ValidateQuoteResponse> ValidateQuote(ValidateQuoteRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.QuoteId, out var quoteId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "quote_id invalide."));
        }

        var validation = await _pricing.ValidateQuoteAsync(quoteId, context.CancellationToken);
        return ToValidationProto(validation);
    }

    /// <summary>
    /// Relit un devis établi, avec de quoi le REFUSER en connaissance de cause.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `GetQuoteAsync` FAIT PLUS QUE LIRE : IL PÉRIME.
    ///
    /// Il bascule un devis `ACTIVE` dont l'échéance est passée en `EXPIRED`, et
    /// l'écrit. C'est pour cela que l'on passe par lui plutôt que par une lecture
    /// nue : sans cela, un devis expiré depuis une heure serait rendu `ACTIVE`
    /// avec une `ExpiresAt` dans le passé, et il faudrait que chaque appelant
    /// refasse la comparaison — avec son horloge, et sa dérive.
    ///
    /// `found = false` N'EST PAS UNE ERREUR. Un identifiant recopié de travers
    /// ou un devis purgé sont des cas ordinaires du checkout. Les rendre en
    /// `NotFound` obligerait l'appelant à distinguer « ce devis n'existe pas » de
    /// « ce service est tombé » en lisant un code de statut.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public override async Task<LookupQuoteResponse> LookupQuote(
        LookupQuoteRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.QuoteId, out var quoteId))
        {
            // Un identifiant illisible n'est pas un devis introuvable : c'est un
            // appelant fautif, et le lui dire évite qu'il cherche du côté du devis.
            throw new RpcException(new Status(StatusCode.InvalidArgument, "quote_id invalide."));
        }

        var quote = await _pricing.GetQuoteAsync(quoteId, context.CancellationToken);

        if (quote is null)
        {
            return new LookupQuoteResponse { Found = false };
        }

        return new LookupQuoteResponse
        {
            Found = true,
            QuoteId = quote.Id.ToString(),
            Total = quote.Total,
            Currency = quote.Currency,

            // ARRONDI AU PLUS PROCHE, PAS TRONCATURE. 90 secondes est une
            // course « d'environ 2 minutes », pas « d'environ 1 minute » : c'est
            // ce qui s'affiche au client, et une minute annoncée en moins se
            // remarque à chaque commande.
            EstimatedMinutes = (int)Math.Round(quote.DurationSeconds / 60.0, MidpointRounding.AwayFromZero),
            DistanceKm = quote.DistanceMeters / 1000.0,
            ExpiresAt = quote.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),

            // DEUX ÉTATS DISTINCTS, JAMAIS FONDUS EN « INVALIDE » — voir le
            // proto. `GetQuoteAsync` a déjà périmé le devis si son échéance est
            // passée, donc le statut lu ici fait foi.
            IsExpired = quote.Status == "EXPIRED",
            IsConsumed = quote.Status == "CONSUMED",

            PickupLatitude = quote.Pickup.Latitude,
            PickupLongitude = quote.Pickup.Longitude,
            DropoffLatitude = quote.Dropoff.Latitude,
            DropoffLongitude = quote.Dropoff.Longitude,
            DeliveryType = quote.ServiceLevel
        };
    }

    private static ValidateQuoteResponse ToValidationProto(QuoteValidation validation)
    {
        var response = new ValidateQuoteResponse
        {
            QuoteId = validation.QuoteId.ToString(),
            Valid = validation.Valid,
            Status = validation.Status
        };

        if (validation.Total is { } total)
        {
            response.Total = total;
        }

        if (!string.IsNullOrWhiteSpace(validation.Currency))
        {
            response.Currency = validation.Currency;
        }

        return response;
    }

    public override async Task<GetServiceabilityResponse> GetServiceability(
        GetServiceabilityRequest request,
        ServerCallContext context)
    {
        var serviceability = await _pricing.GetServiceabilityAsync(new ServiceabilityRequest(
            FromProto(request.Pickup),
            FromProto(request.Dropoff)),
            context.CancellationToken);

        var response = new GetServiceabilityResponse
        {
            Serviceable = serviceability.Serviceable,
            DistanceMeters = serviceability.DistanceMeters
        };

        if (!string.IsNullOrWhiteSpace(serviceability.Reason))
        {
            response.Reason = serviceability.Reason;
        }

        return response;
    }

    private static Guid? ParseOptionalGuid(bool hasValue, string value) =>
        hasValue && Guid.TryParse(value, out var parsed) ? parsed : null;

    private static HBA.Delivery.Pricing.Domain.ValueObjects.GeoPoint FromProto(HBA.DeliveryPricing.Grpc.V1.GeoPoint point) =>
        new(point.Latitude, point.Longitude);

    private static DeliveryQuoteReply ToProto(DeliveryQuote quote)
    {
        var response = new DeliveryQuoteReply
        {
            QuoteId = quote.Id.ToString(),
            DistanceMeters = quote.DistanceMeters,
            DurationSeconds = quote.DurationSeconds,
            Subtotal = quote.Subtotal,
            Discount = quote.Discount,
            Total = quote.Total,
            Currency = quote.Currency,
            ExpiresAt = quote.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            PricingVersion = quote.PricingVersion,
            Status = quote.Status,
            ServiceLevel = quote.ServiceLevel
        };

        if (quote.SellerId is { } sellerId)
        {
            response.SellerId = sellerId.ToString();
        }

        if (quote.StoreId is { } storeId)
        {
            response.StoreId = storeId.ToString();
        }

        if (!string.IsNullOrWhiteSpace(quote.VehicleType))
        {
            response.VehicleType = quote.VehicleType;
        }

        return response;
    }
}
