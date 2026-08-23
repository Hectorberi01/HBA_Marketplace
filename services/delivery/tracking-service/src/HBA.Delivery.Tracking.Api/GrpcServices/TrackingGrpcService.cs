using System.Globalization;
using Grpc.Core;
using HBA.Tracking.Application;
using HBA.Tracking.Grpc.V1;
using HBA.Shared.IntegrationEvents;

namespace HBA.Tracking.Api.Grpc;

public sealed class TrackingGrpcService : TrackingApi.TrackingApiBase
{
    private readonly TrackingStore _tracking;
    private readonly IIntegrationEventPublisher _publisher;

    public TrackingGrpcService(TrackingStore tracking, IIntegrationEventPublisher publisher)
    {
        _tracking = tracking;
        _publisher = publisher;
    }

    public override Task<GetLatestLocationResponse> GetLatestLocation(
        GetLatestLocationRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id invalide."));
        }

        return Task.FromResult(_tracking.TryGetLatest(deliveryId, out var snapshot)
            ? new GetLatestLocationResponse { Found = true, Snapshot = ToProto(snapshot!) }
            : new GetLatestLocationResponse { Found = false });
    }

    public override async Task<TrackingSessionReply> StartTrackingSession(
        HBA.Tracking.Grpc.V1.StartTrackingSessionRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId) || !Guid.TryParse(request.DriverId, out var driverId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id ou driver_id invalide."));
        }

        return ToProto(await _tracking.StartAsync(deliveryId, driverId, _publisher, context.CancellationToken), found: true);
    }

    public override async Task<TrackingSessionReply> StopTrackingSession(
        StopTrackingSessionRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id invalide."));
        }

        var session = await _tracking.StopAsync(deliveryId, _publisher, context.CancellationToken);
        return session is null ? new TrackingSessionReply { Found = false } : ToProto(session, found: true);
    }

    private static HBA.Tracking.Grpc.V1.TrackingSnapshot ToProto(Application.TrackingSnapshot snapshot)
    {
        var response = new HBA.Tracking.Grpc.V1.TrackingSnapshot
        {
            DeliveryId = snapshot.DeliveryId.ToString(),
            DriverId = snapshot.DriverId.ToString(),
            Latitude = snapshot.Latitude,
            Longitude = snapshot.Longitude,
            CapturedAt = snapshot.CapturedAt.ToString("O", CultureInfo.InvariantCulture)
        };

        if (snapshot.EtaSeconds is { } eta)
        {
            response.EtaSeconds = eta;
        }

        return response;
    }

    private static TrackingSessionReply ToProto(TrackingSession session, bool found)
    {
        var response = new TrackingSessionReply
        {
            Found = found,
            SessionId = session.Id.ToString(),
            DeliveryId = session.DeliveryId.ToString(),
            DriverId = session.DriverId.ToString(),
            Status = session.Status,
            StartedAt = session.StartedAt.ToString("O", CultureInfo.InvariantCulture),
            LastSequence = session.LastSequence
        };

        if (session.EndedAt is { } endedAt)
        {
            response.EndedAt = endedAt.ToString("O", CultureInfo.InvariantCulture);
        }

        return response;
    }
}
