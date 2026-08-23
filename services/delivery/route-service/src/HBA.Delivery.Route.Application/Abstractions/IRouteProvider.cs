namespace HBA.Delivery.Route.Application;

public interface IRouteProvider
{
    Task<RouteProviderEstimate> EstimateAsync(
        RouteProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record RouteProviderRequest(
    double OriginLatitude,
    double OriginLongitude,
    double DestinationLatitude,
    double DestinationLongitude,
    string? VehicleMode);

public sealed record RouteProviderEstimate(
    string Provider,
    int DistanceMeters,
    int DurationSeconds,
    string? EncodedPolyline);
