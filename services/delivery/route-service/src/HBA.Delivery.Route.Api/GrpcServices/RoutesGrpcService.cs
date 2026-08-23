using System.Globalization;
using Grpc.Core;
using HBA.Routes.Application;
using HBA.Routes.Grpc.V1;
using HBA.Shared.IntegrationEvents;

namespace HBA.Routes.Api.Grpc;

public sealed class RoutesGrpcService : RouteApi.RouteApiBase
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
