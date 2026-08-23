using System.Collections.Concurrent;
using HBA.Routes.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Routes.Application;

public sealed class RouteStore
{
    private readonly ConcurrentDictionary<Guid, RoutePlan> _plans = new();

    public async Task<RoutePlan> EstimateAsync(
        EstimateRouteRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var distance = HaversineMeters(request.Origin, request.Destination);
        var duration = Math.Max(60, (int)(distance / 5.8));
        var route = new RoutePlan(
            Guid.NewGuid(),
            request.DeliveryId,
            "FALLBACK_HAVERSINE",
            request.Origin,
            request.Destination,
            request.Waypoints,
            distance,
            duration,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(20),
            DateTimeOffset.UtcNow.AddSeconds(duration));

        if (request.DeliveryId is { } deliveryId)
        {
            _plans[deliveryId] = route;
        }

        await publisher.PublishAsync(new RouteCalculatedIntegrationEvent
        {
            RoutePlanId = route.Id,
            DeliveryId = route.DeliveryId,
            DistanceMeters = route.DistanceMeters,
            DurationSeconds = route.DurationSeconds
        }, cancellationToken);

        return route;
    }

    public Task<RoutePlan> OptimizeAsync(
        OptimizeRouteRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var orderedStops = request.Stops.OrderBy(stop => HaversineMeters(request.Origin, stop)).ToArray();
        var destination = orderedStops.LastOrDefault() ?? request.Origin;
        return EstimateAsync(new EstimateRouteRequest(request.DeliveryId, request.Origin, destination, orderedStops, request.VehicleMode), publisher, cancellationToken);
    }

    public async Task<EtaSnapshot> RecalculateEtaAsync(
        RecalculateEtaRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var distance = HaversineMeters(request.CurrentPosition, request.Destination);
        var duration = Math.Max(60, (int)(distance / 5.8));
        var snapshot = new EtaSnapshot(Guid.NewGuid(), request.DeliveryId, request.RoutePlanId, DateTimeOffset.UtcNow.AddSeconds(duration), distance, DateTimeOffset.UtcNow, "FALLBACK_HAVERSINE");

        await publisher.PublishAsync(new RouteRecalculatedIntegrationEvent
        {
            DeliveryId = request.DeliveryId,
            RoutePlanId = request.RoutePlanId,
            EtaAtUtc = snapshot.EtaAt.UtcDateTime
        }, cancellationToken);

        await publisher.PublishAsync(new RouteDeliveryEtaUpdatedIntegrationEvent
        {
            DeliveryId = request.DeliveryId,
            EtaAtUtc = snapshot.EtaAt.UtcDateTime
        }, cancellationToken);

        return snapshot;
    }

    public bool TryGet(Guid deliveryId, out RoutePlan? route) => _plans.TryGetValue(deliveryId, out route);

    private static int HaversineMeters(GeoPoint a, GeoPoint b)
    {
        const double earthRadius = 6371000;
        var dLat = DegreesToRadians(b.Latitude - a.Latitude);
        var dLon = DegreesToRadians(b.Longitude - a.Longitude);
        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return (int)Math.Round(earthRadius * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h)));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;
}

public sealed record GeoPoint(double Latitude, double Longitude);
public sealed record EstimateRouteRequest(Guid? DeliveryId, GeoPoint Origin, GeoPoint Destination, IReadOnlyList<GeoPoint> Waypoints, string? VehicleMode);
public sealed record OptimizeRouteRequest(Guid? DeliveryId, GeoPoint Origin, IReadOnlyList<GeoPoint> Stops, string? VehicleMode);
public sealed record RecalculateEtaRequest(Guid DeliveryId, Guid RoutePlanId, GeoPoint CurrentPosition, GeoPoint Destination);
public sealed record RoutePlan(Guid Id, Guid? DeliveryId, string Provider, GeoPoint Origin, GeoPoint Destination, IReadOnlyList<GeoPoint> Waypoints, int DistanceMeters, int DurationSeconds, string? EncodedPolyline, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset EtaAt);
public sealed record EtaSnapshot(Guid Id, Guid DeliveryId, Guid RoutePlanId, DateTimeOffset EtaAt, int RemainingMeters, DateTimeOffset ComputedAt, string Source);
