using HBA.Promotion.Grpc.V1;

namespace HBA.Promotions.Contracts.Grpc;

/// <summary>
/// Côté CLIENT : implémente <see cref="IPromotionModuleApi"/> par gRPC.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CODE APPELANT NE CHANGE PAS.
///
/// cart-service, order-service et food-order-service écrivent
/// `_promotions.ReserveAsync(...)` sans savoir si l'implémentation est en
/// processus ou au bout d'un socket. C'est ce qui rend l'extraction réversible :
/// rebrancher l'implémentation locale se fait par une ligne d'enregistrement DI.
///
/// AUCUNE `RpcException` N'EST AVALÉE ICI.
///
/// La tentation est de l'attraper et de rendre « coupon invalide ». L'appelant ne
/// distinguerait alors plus « ce code ne s'applique pas » de « promotion-service
/// est à terre » — et le checkout retirerait au client une remise à laquelle il a
/// droit, sans laisser de trace. Un refus métier arrive déjà par une RÉPONSE
/// (`valid: false`) ; ce qui remonte en exception est une vraie panne, et doit
/// remonter.
///
/// La politique de résilience — délai, reprise, disjoncteur — se pose à
/// l'enregistrement du client, pas ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class PromotionGrpcClient : IPromotionModuleApi
{
    private readonly PromotionApi.PromotionApiClient _client;

    public PromotionGrpcClient(PromotionApi.PromotionApiClient client) => _client = client;

    public async Task<PromotionEvaluationResult> EvaluateAsync(
        string code, PromotionEvaluationContext context, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.EvaluatePromotionAsync(
            new EvaluatePromotionRequest { Code = code ?? string.Empty, Context = context.ToProto() },
            cancellationToken: cancellationToken);

        return reponse.ToContract();
    }

    public async Task<CouponReservationResult?> ReserveAsync(
        string code, Guid userId, Guid cartId, PromotionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var reponse = await _client.ReserveCouponAsync(
            new ReserveCouponRequest
            {
                Code = code ?? string.Empty,
                UserId = userId.ToString(),
                CartId = cartId.ToString(),
                Context = context.ToProto()
            },
            cancellationToken: cancellationToken);

        return reponse.ToContract();
    }

    public async Task<bool> CommitAsync(
        Guid reservationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.CommitCouponAsync(
            new CommitCouponRequest
            {
                ReservationId = reservationId.ToString(),
                OrderId = orderId.ToString()
            },
            cancellationToken: cancellationToken);

        return reponse.Committed;
    }

    public async Task<bool> ReleaseAsync(
        Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.ReleaseCouponAsync(
            new ReleaseCouponRequest { ReservationId = reservationId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.Released;
    }
}
