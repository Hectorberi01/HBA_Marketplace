using System.Globalization;
using Grpc.Core;
using HBA.ProofOfDelivery.Application;
using HBA.ProofOfDelivery.Grpc.V1;

namespace HBA.ProofOfDelivery.Api.Grpc;

public sealed class ProofGrpcService : ProofApi.ProofApiBase
{
    private readonly ProofStore _proofs;

    public ProofGrpcService(ProofStore proofs) => _proofs = proofs;

    public override Task<HasValidDropoffProofResponse> HasValidDropoffProof(
        HasValidDropoffProofRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id invalide."));
        }

        return Task.FromResult(new HasValidDropoffProofResponse
        {
            DeliveryId = request.DeliveryId,
            Valid = _proofs.HasValidDropoffProof(deliveryId)
        });
    }

    public override Task<GetProofSummaryResponse> GetProofSummary(
        GetProofSummaryRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var deliveryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id invalide."));
        }

        var response = new GetProofSummaryResponse();
        response.Proofs.AddRange(_proofs.ListByDelivery(deliveryId).Select(ToProto));
        return Task.FromResult(response);
    }

    private static HBA.ProofOfDelivery.Grpc.V1.ProofSummary ToProto(Application.ProofSummary summary)
    {
        var proof = summary.Proof;
        var response = new HBA.ProofOfDelivery.Grpc.V1.ProofSummary
        {
            ProofId = proof.Id.ToString(),
            DeliveryId = proof.DeliveryId.ToString(),
            Type = proof.Type,
            Status = proof.Status,
            OtpVerified = proof.OtpVerified,
            CreatedAt = proof.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            DriverId = proof.DriverId.ToString()
        };

        if (proof.StopId is { } stopId)
        {
            response.StopId = stopId.ToString();
        }

        if (!string.IsNullOrWhiteSpace(proof.RecipientName))
        {
            response.RecipientName = proof.RecipientName;
        }

        response.Media.AddRange(summary.Media.Select(media => new HBA.ProofOfDelivery.Grpc.V1.ProofMedia
        {
            Id = media.Id.ToString(),
            MediaType = media.MediaType,
            ObjectKey = media.ObjectKey,
            MimeType = media.MimeType,
            Sha256 = media.Sha256,
            CapturedAt = media.CapturedAt.ToString("O", CultureInfo.InvariantCulture),
            SizeBytes = media.SizeBytes
        }));

        return response;
    }
}
