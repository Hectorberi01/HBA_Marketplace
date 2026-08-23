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
        //
        // Créer un devis coûte un calcul et laisse une ligne en base ; la
        // couverture et les zones décrivent l'implantation commerciale de la
        // plateforme. Rien de tout cela n'a à être servi à un inconnu — et
        // `MapGroup` nu ne demandait rien du tout.
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

        // ═════════════════════════════════════════════════════════════════════
        // CES ROUTES FIXAIENT LE PRIX DES COURSES SANS LE MOINDRE JETON.
        //
        // `MapGroup` nu, dans un hôte qui n'appelait ni `UseAuthentication` ni
        // `UseAuthorization` : n'importe qui pouvait créer, modifier et activer
        // une règle de tarification — donc décider ce que la plateforme facture
        // à ses clients et reverse à ses livreurs.
        //
        // Deux verrous, pas un : `AddHbaSecurity` dans `Program.cs` ferme
        // l'anonymat sur tout l'hôte ; `MapAdminGroup` réserve CE groupe au
        // back-office. Le premier seul laisserait un acheteur quelconque toucher
        // à la grille tarifaire.
        //
        // Le commentaire d'en-tête d'`ApiAuthorization` pose la règle :
        // « tout nouveau groupe part de `MapAdminGroup` ou `MapAuthenticatedGroup`,
        // jamais de `MapGroup` nu ». Elle n'avait simplement pas été suivie ici.
        // ═════════════════════════════════════════════════════════════════════
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
