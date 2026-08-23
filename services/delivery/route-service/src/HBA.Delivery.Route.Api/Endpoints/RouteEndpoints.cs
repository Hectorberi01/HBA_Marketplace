using HBA.Routes.Application;
using HBA.Shared.Hosting.Http;
using HBA.Shared.IntegrationEvents;

namespace HBA.Routes.Api.Endpoints;

public static class RouteEndpoints
{
    public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/v1/routes").WithTags("Routes");

        routes.MapPost("/estimate", async (
            EstimateRouteRequest request,
            RouteStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.EstimateAsync(request, publisher, cancellationToken))));

        routes.MapPost("/optimize", async (
            OptimizeRouteRequest request,
            RouteStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.OptimizeAsync(request, publisher, cancellationToken))));

        routes.MapGet("/deliveries/{deliveryId:guid}", (Guid deliveryId, RouteStore store) =>
            store.TryGet(deliveryId, out var route)
                ? Results.Ok(ApiEnvelope.Ok(route))
                : Results.NotFound(ApiEnvelope.Fail("ROUTE_NOT_FOUND", "Itineraire introuvable.")));

        var internalApi = app.MapGroup("/internal/v1/routes").WithTags("Routes - Internal");

        internalApi.MapPost("/estimate", async (
            EstimateRouteRequest request,
            RouteStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.EstimateAsync(request, publisher, cancellationToken))));

        internalApi.MapPost("/optimize", async (
            OptimizeRouteRequest request,
            RouteStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.OptimizeAsync(request, publisher, cancellationToken))));

        internalApi.MapPost("/eta", async (
            RecalculateEtaRequest request,
            RouteStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
            Results.Ok(ApiEnvelope.Ok(await store.RecalculateEtaAsync(request, publisher, cancellationToken))));

        return app;
    }
}
