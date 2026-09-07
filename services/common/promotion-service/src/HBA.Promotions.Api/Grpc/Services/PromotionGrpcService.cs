using Grpc.Core;
using HBA.Promotion.Grpc.V1;

using HBA.Promotions.Api.Grpc.Mappers;
using HBA.Promotions.Contracts;
// DEPLACE DEPUIS `HBA.Promotions.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.Promotions.Api.Grpc.Services;

/// <summary>Côté SERVEUR : expose <see cref="IPromotionModuleApi"/> sur le port gRPC.</summary>
internal sealed class PromotionGrpcService : PromotionApi.PromotionApiBase
{
    private readonly IPromotionModuleApi _promotions;

    public PromotionGrpcService(IPromotionModuleApi promotions) => _promotions = promotions;

    public override async Task<EvaluatePromotionResponse> EvaluatePromotion(
        EvaluatePromotionRequest request, ServerCallContext context)
    {
        var resultat = await _promotions.EvaluateAsync(
            request.Code, request.Context.ToContract(), context.CancellationToken);

        return resultat.ToProto();
    }

    public override async Task<ReserveCouponResponse> ReserveCoupon(
        ReserveCouponRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        if (!Guid.TryParse(request.CartId, out var cartId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cart_id n'est pas un GUID."));
        }

        var retenue = await _promotions.ReserveAsync(
            request.Code, userId, cartId, request.Context.ToContract(), context.CancellationToken);

        // ON RÉÉVALUE POUR DIRE POURQUOI, PLUTÔT QUE DE RENDRE UN REFUS MUET.
        if (retenue is null)
        {
            var motif = await _promotions.EvaluateAsync(
                request.Code, request.Context.ToContract(), context.CancellationToken);

            return retenue.ToProto(motif.Reason ?? "promotions.coupon.not_applicable");
        }

        return retenue.ToProto(null);
    }

    public override async Task<CommitCouponResponse> CommitCoupon(
        CommitCouponRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ReservationId, out var reservationId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "reservation_id n'est pas un GUID."));
        }

        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "order_id n'est pas un GUID."));
        }

        var engage = await _promotions.CommitAsync(reservationId, orderId, context.CancellationToken);

        return new CommitCouponResponse
        {
            Committed = engage,
            Reason = engage ? string.Empty : "promotions.coupon.commit_failed"
        };
    }

    public override async Task<ReleaseCouponResponse> ReleaseCoupon(
        ReleaseCouponRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ReservationId, out var reservationId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "reservation_id n'est pas un GUID."));
        }

        var libere = await _promotions.ReleaseAsync(reservationId, context.CancellationToken);

        return new ReleaseCouponResponse
        {
            Released = libere,
            Reason = libere ? string.Empty : "promotions.coupon.release_failed"
        };
    }
}
