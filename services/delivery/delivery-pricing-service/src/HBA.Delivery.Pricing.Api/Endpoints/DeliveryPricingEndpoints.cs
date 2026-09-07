using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Application.DTOs;
using HBA.Delivery.Pricing.Domain.ValueObjects;
using HBA.Shared.Hosting.Http;
using HBA.Shared.IntegrationEvents;

namespace HBA.Delivery.Pricing.Api.Endpoints;

public static class DeliveryPricingEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryPricingEndpoints(this IEndpointRouteBuilder app)
    {
        // AUTHENTIFIÉ, PAS ANONYME.
        var pricing = app.MapAuthenticatedGroup("/api/v1/delivery-pricing").WithTags("Delivery Pricing");

        pricing.MapPost("/quotes", async (
            CreateQuoteRequest request,
            IPricingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var quote = await store.CreateQuoteAsync(request, publisher, cancellationToken);
            return Results.Created($"/api/v1/delivery-pricing/quotes/{quote.Id}", ApiEnvelope.Ok(quote));
        });

        pricing.MapGet("/quotes/{id:guid}", async (Guid id, IPricingStore store, CancellationToken cancellationToken) =>
        {
            var quote = await store.GetQuoteAsync(id, cancellationToken);
            return quote is not null
                ? Results.Ok(ApiEnvelope.Ok(quote))
                : Results.NotFound(ApiEnvelope.Fail("DELIVERY_QUOTE_NOT_FOUND", "Devis de livraison introuvable."));
        });

        pricing.MapGet("/serviceability", async (
            double pickupLatitude,
            double pickupLongitude,
            double dropoffLatitude,
            double dropoffLongitude,
            IPricingStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.GetServiceabilityAsync(new ServiceabilityRequest(
                new GeoPoint(pickupLatitude, pickupLongitude),
                new GeoPoint(dropoffLatitude, dropoffLongitude)), cancellationToken))));

        pricing.MapGet("/zones", async (IPricingStore store, CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.ListZonesAsync(cancellationToken))));

        // CES ROUTES FIXAIENT LE PRIX DES COURSES SANS LE MOINDRE JETON.
        var admin = app.MapAdminGroup("/api/v1/admin/delivery-pricing").WithTags("Delivery Pricing · Admin");

        admin.MapGet("/rules", async (IPricingStore store, CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.ListRulesAsync(cancellationToken))));

        admin.MapPost("/rules", async (
            PricingRuleRequest request,
            IPricingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var rule = await store.AddRuleAsync(request, publisher, cancellationToken);
            return Results.Created($"/api/v1/admin/delivery-pricing/rules/{rule.Id}", ApiEnvelope.Ok(rule));
        });

        admin.MapPatch("/rules/{id:guid}", async (
            Guid id,
            PricingRuleRequest request,
            IPricingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var rule = await store.UpdateRuleAsync(id, request, publisher, cancellationToken);
            return rule is null
                ? Results.NotFound(ApiEnvelope.Fail("PRICING_RULE_NOT_FOUND", "Règle tarifaire introuvable."))
                : Results.Ok(ApiEnvelope.Ok(rule));
        });

        admin.MapPost("/rules/{id:guid}/activate", async (
            Guid id,
            IPricingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var rule = await store.SetRuleStatusAsync(id, active: true, publisher, cancellationToken);
            return rule is null
                ? Results.NotFound(ApiEnvelope.Fail("PRICING_RULE_NOT_FOUND", "Règle tarifaire introuvable."))
                : Results.Ok(ApiEnvelope.Ok(rule));
        });

        admin.MapPost("/rules/{id:guid}/deactivate", async (
            Guid id,
            IPricingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var rule = await store.SetRuleStatusAsync(id, active: false, publisher, cancellationToken);
            return rule is null
                ? Results.NotFound(ApiEnvelope.Fail("PRICING_RULE_NOT_FOUND", "Règle tarifaire introuvable."))
                : Results.Ok(ApiEnvelope.Ok(rule));
        });

        var internalApi = app.MapGroup("/internal/v1/delivery-pricing").WithTags("Delivery Pricing · Internal");

        internalApi.MapPost("/quote", async (
            CreateQuoteRequest request,
            IPricingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.CreateQuoteAsync(request, publisher, cancellationToken))));

        internalApi.MapGet("/quotes/{id:guid}/validate", async (
            Guid id,
            IPricingStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.ValidateQuoteAsync(id, cancellationToken))));

        internalApi.MapPost("/quotes/{id:guid}/consume/{deliveryId:guid}", async (
            Guid id,
            Guid deliveryId,
            IPricingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.ConsumeQuoteAsync(id, deliveryId, publisher, cancellationToken))));

        internalApi.MapPost("/serviceability", async (
            ServiceabilityRequest request,
            IPricingStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.GetServiceabilityAsync(request, cancellationToken))));

        return app;
    }
}
