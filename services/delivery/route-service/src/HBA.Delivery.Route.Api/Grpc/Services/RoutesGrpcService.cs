using Grpc.Core;
using HBA.Routes.Application;
using HBA.Routes.Grpc.V1;
using HBA.Shared.IntegrationEvents;

using System.Globalization;

// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Routes.Api.Grpc` (lot B de la migration gRPC).
//
// LE SERVEUR VIVAIT DANS L'ASSEMBLAGE DE CONTRATS, DONC CHEZ TOUS SES
// CONSOMMATEURS. Les dix services qui consomment merchant.proto liaient
// l'implementation de seller-service ; les huit qui consomment order.proto
// liaient celle d'order-service. Aucun ne s'en servait.
//
// Le serveur est la surface d'UN service : il vit desormais dans son `.Api`.
// L'assemblage de contrats ne porte plus que le stub genere, le client et son
// enregistrement — le lot C descendra ces deux-la chez les appelants.
//
// CE QUE ÇA NE CHANGE PAS : le cablage. `Program.cs` appelle toujours
// `MapInternalGrpcService<...>()`, avec la meme autorisation et les memes
// intercepteurs. Un deplacement de fichier ne rend rien plus sur.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Routes.Api.Grpc.Services;

internal sealed class RoutesGrpcService : RouteApi.RouteApiBase
{
    private readonly RouteStore _routes;
    private readonly IIntegrationEventPublisher _publisher;

    public RoutesGrpcService(RouteStore routes, IIntegrationEventPublisher publisher)
    {
        _routes = routes;
        _publisher = publisher;
    }

    public override async Task<RoutePlanReply> EstimateRoute(
        HBA.Routes.Grpc.V1.EstimateRouteRequest request,
        ServerCallContext context)
    {
        var deliveryId = request.HasDeliveryId && Guid.TryParse(request.DeliveryId, out var parsed) ? parsed : (Guid?)null;
        var route = await _routes.EstimateAsync(new Application.EstimateRouteRequest(
            deliveryId,
            FromProto(request.Origin),
            FromProto(request.Destination),
            request.Waypoints.Select(FromProto).ToArray(),
            request.HasVehicleMode ? request.VehicleMode : null),
            _publisher,
            context.CancellationToken);

        return ToProto(route);
    }

    public override async Task<RoutePlanReply> OptimizeRoute(
        HBA.Routes.Grpc.V1.OptimizeRouteRequest request,
        ServerCallContext context)
    {
        var deliveryId = request.HasDeliveryId && Guid.TryParse(request.DeliveryId, out var parsed) ? parsed : (Guid?)null;
        var route = await _routes.OptimizeAsync(new Application.OptimizeRouteRequest(
            deliveryId,
            FromProto(request.Origin),
            request.Stops.Select(FromProto).ToArray(),
            request.HasVehicleMode ? request.VehicleMode : null),
            _publisher,
            context.CancellationToken);

        return ToProto(route);
    }

    public override async Task<EtaSnapshotReply> RecalculateEta(
        HBA.Routes.Grpc.V1.RecalculateEtaRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId) || !Guid.TryParse(request.RoutePlanId, out var routePlanId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id ou route_plan_id invalide."));
        }

        var eta = await _routes.RecalculateEtaAsync(new Application.RecalculateEtaRequest(
            deliveryId,
            routePlanId,
            FromProto(request.CurrentPosition),
            FromProto(request.Destination)),
            _publisher,
            context.CancellationToken);

        return new EtaSnapshotReply
        {
            Id = eta.Id.ToString(),
            DeliveryId = eta.DeliveryId.ToString(),
            RoutePlanId = eta.RoutePlanId.ToString(),
            EtaAt = eta.EtaAt.ToString("O", CultureInfo.InvariantCulture),
            RemainingMeters = eta.RemainingMeters,
            ComputedAt = eta.ComputedAt.ToString("O", CultureInfo.InvariantCulture),
            Source = eta.Source
        };
    }

    private static Application.GeoPoint FromProto(HBA.Routes.Grpc.V1.GeoPoint point) => new(point.Latitude, point.Longitude);

    private static RoutePlanReply ToProto(RoutePlan route)
    {
        var response = new RoutePlanReply
        {
            Id = route.Id.ToString(),
            Provider = route.Provider,
            Origin = ToProto(route.Origin),
            Destination = ToProto(route.Destination),
            DistanceMeters = route.DistanceMeters,
            DurationSeconds = route.DurationSeconds,
            CreatedAt = route.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            ExpiresAt = route.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            EtaAt = route.EtaAt.ToString("O", CultureInfo.InvariantCulture)
        };

        if (route.DeliveryId is { } deliveryId)
        {
            response.DeliveryId = deliveryId.ToString();
        }

        if (!string.IsNullOrWhiteSpace(route.EncodedPolyline))
        {
            response.EncodedPolyline = route.EncodedPolyline;
        }

        response.Waypoints.AddRange(route.Waypoints.Select(ToProto));
        return response;
    }

    private static HBA.Routes.Grpc.V1.GeoPoint ToProto(Application.GeoPoint point) => new()
    {
        Latitude = point.Latitude,
        Longitude = point.Longitude
    };
}
