using Grpc.Core;
using HBA.Promotion.Grpc.V1;

namespace HBA.Promotions.Contracts.Grpc;

/// <summary>
/// Côté SERVEUR : expose <see cref="IPromotionModuleApi"/> sur le port gRPC.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE CLASSE NE DÉCIDE DE RIEN.
///
/// Elle traduit, appelle, retraduit. Une règle ajoutée ici — un contrôle
/// d'éligibilité, un plafond — serait invisible à la route REST, qui passe par le
/// même `IPromotionModuleApi` sans traverser ce fichier. Le panier validé à
/// l'écran serait alors refusé au checkout, ou l'inverse.
///
/// UN COUPON REFUSÉ REND UNE RÉPONSE, JAMAIS UN `RpcException`.
///
/// C'est la règle la plus importante ici, et elle vaut de l'argent. Saisir un code
/// périmé est un usage ordinaire du champ. Lever ferait deux dégâts : chaque
/// frappe d'un client apparaîtrait dans les compteurs d'erreur du service, et
/// surtout le DISJONCTEUR de l'appelant s'ouvrirait — coupant les évaluations
/// valides de tous les autres clients pendant la durée de réarmement. C'est la
/// même leçon que la validation de jeton d'identity-service.
///
/// `RpcException` est donc réservé à ce qui est vraiment une faute de l'appelant :
/// un identifiant malformé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class PromotionGrpcService : PromotionApi.PromotionApiBase
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
        //
        // `ReserveAsync` rend `null` sans motif. Un « reserved: false » nu
        // obligerait le checkout à afficher « coupon refusé » sans dire si le
        // panier est trop petit, le coupon épuisé, ou la campagne terminée — et
        // c'est la seule information que le client puisse utiliser.
        //
        // L'appel supplémentaire est en LECTURE PURE et n'a lieu que sur le chemin
        // d'échec, qui est le chemin rare.
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
