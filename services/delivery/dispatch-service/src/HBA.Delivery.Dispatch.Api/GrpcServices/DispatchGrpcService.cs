using System.Globalization;
using Grpc.Core;
using HBA.Dispatch.Application;
using HBA.Dispatch.Grpc.V1;
using HBA.Shared.IntegrationEvents;

namespace HBA.Dispatch.Api.Grpc;

public sealed class DispatchGrpcService : DispatchApi.DispatchApiBase
{
    private readonly DispatchStore _dispatch;
    private readonly IIntegrationEventPublisher _publisher;

    public DispatchGrpcService(DispatchStore dispatch, IIntegrationEventPublisher publisher)
    {
        _dispatch = dispatch;
        _publisher = publisher;
    }

    public override async Task<DispatchJobReply> RequestDispatch(
        HBA.Dispatch.Grpc.V1.RequestDispatchRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id invalide."));
        }

        var job = await _dispatch.RequestAsync(new Application.RequestDispatchRequest(
            deliveryId,
            FromProto(request.Pickup),
            FromProto(request.Dropoff),
            request.HasVehicleRequirement ? request.VehicleRequirement : null,
            request.Priority),
            _publisher,
            context.CancellationToken);

        return ToProto(job);
    }

    public override Task<CancelDispatchResponse> CancelDispatch(CancelDispatchRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id invalide."));
        }

        _dispatch.Cancel(deliveryId);
        return Task.FromResult(new CancelDispatchResponse { Cancelled = true });
    }

    public override Task<GetAssignmentResponse> GetAssignment(GetAssignmentRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id invalide."));
        }

        return Task.FromResult(_dispatch.TryGetAssignment(deliveryId, out var assignment)
            ? new GetAssignmentResponse { Found = true, Assignment = ToProto(assignment!) }
            : new GetAssignmentResponse { Found = false });
    }

    public override async Task<GetAssignmentResponse> AcceptOffer(AcceptOfferRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId) || !Guid.TryParse(request.DriverId, out var driverId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id ou driver_id invalide."));
        }

        // LA PREMIÈRE ACCEPTATION GAGNE, LA SECONDE EST REFUSÉE — ISSUE-028.
        //
        // `RequestAsync` propose la course à PLUSIEURS candidats à la fois : deux
        // acceptations concurrentes sont le cas NOMINAL, pas le cas de bord. La
        // version d'origine rendait `Found = true` aux deux, et les deux livreurs
        // partaient.
        //
        // `FailedPrecondition` et non `AlreadyExists` : la course existe et son
        // affectation aussi — c'est l'ÉTAT du système qui rend l'opération
        // impossible, et c'est le code que l'application livreur doit traduire par
        // « quelqu'un a été plus rapide », pas par une erreur technique.
        var (assigned, assignment) = await _dispatch.AssignAsync(
            deliveryId, driverId, "AUTO", _publisher, context.CancellationToken);

        if (!assigned)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "Cette course a déjà été acceptée par un autre livreur."));
        }

        return new GetAssignmentResponse { Found = true, Assignment = ToProto(assignment) };
    }

    private static Application.GeoPoint FromProto(HBA.Dispatch.Grpc.V1.GeoPoint point) => new(point.Latitude, point.Longitude);

    private static DispatchJobReply ToProto(DispatchJob job)
    {
        var response = new DispatchJobReply
        {
            JobId = job.Id.ToString(),
            DeliveryId = job.DeliveryId.ToString(),
            Status = job.Status,
            Attempt = job.Attempt,
            SearchRadiusMeters = job.SearchRadiusMeters,
            ExpiresAt = job.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        };

        response.Candidates.AddRange(job.Candidates.Select(candidate => new HBA.Dispatch.Grpc.V1.DriverCandidate
        {
            DriverId = candidate.DriverId.ToString(),
            DistanceToPickupMeters = candidate.DistanceToPickupMeters,
            EtaSeconds = candidate.EtaSeconds,
            Score = candidate.Score.ToString(CultureInfo.InvariantCulture),
            Rank = candidate.Rank
        }));

        return response;
    }

    private static HBA.Dispatch.Grpc.V1.Assignment ToProto(Application.Assignment assignment)
    {
        var response = new HBA.Dispatch.Grpc.V1.Assignment
        {
            Id = assignment.Id.ToString(),
            DeliveryId = assignment.DeliveryId.ToString(),
            DriverId = assignment.DriverId.ToString(),
            AssignedAt = assignment.AssignedAt.ToString("O", CultureInfo.InvariantCulture),
            AssignmentMode = assignment.AssignmentMode,
            Status = assignment.Status
        };

        if (assignment.OfferId is { } offerId)
        {
            response.OfferId = offerId.ToString();
        }

        return response;
    }
}
