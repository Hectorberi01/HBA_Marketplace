using HBA.Promotions.Application.Promotions;
using HBA.Promotions.Contracts;
using HBA.Promotions.Domain.Promotions;
using MediatR;

namespace HBA.Promotions.Infrastructure.Public;

/// <summary>L'IMPLÉMENTATION DE <see cref="IPromotionModuleApi"/>.</summary>
internal sealed class PromotionModuleApi : IPromotionModuleApi
{
    private readonly ISender _sender;

    public PromotionModuleApi(ISender sender) => _sender = sender;

    public async Task<PromotionEvaluationResult> EvaluateAsync(
        string code, PromotionEvaluationContext context, CancellationToken cancellationToken = default)
    {
        var resultat = await _sender.Send(
            new ValidateCouponQuery(
                code, Univers(context.Scope), context.Subtotal, context.DeliveryFee,
                context.Currency, context.UserId),
            cancellationToken);

        // UN ÉCHEC TECHNIQUE N'EST PAS « COUPON INVALIDE », ET LE CONFONDRE
        // MENTIRAIT AU CLIENT.
        if (resultat.IsFailure)
        {
            return new PromotionEvaluationResult(
                false, null, 0, context.Currency,
                "Le service de promotion est momentanément indisponible.",
                resultat.Error.Code);
        }

        var evaluation = resultat.Value;

        // LA DÉCOMPOSITION PAR FINANCEUR TRAVERSE TELLE QUELLE (D28).
        return new PromotionEvaluationResult(
            evaluation.Valid, evaluation.PromotionId, evaluation.Discount,
            evaluation.Currency, evaluation.Message, evaluation.Reason,
            evaluation.SellerFundedDiscount, evaluation.PlatformFundedDiscount,
            evaluation.OwnerSellerId);
    }

    public async Task<CouponReservationResult?> ReserveAsync(
        string code, Guid userId, Guid cartId, PromotionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var resultat = await _sender.Send(
            new ReserveCouponCommand(
                code, userId, cartId, Univers(context.Scope),
                context.Subtotal, context.DeliveryFee, context.Currency),
            cancellationToken);

        if (resultat.IsFailure)
        {
            return null;
        }

        var retenue = resultat.Value;

        return new CouponReservationResult(
            retenue.ReservationId, retenue.CouponId, retenue.PromotionId,
            retenue.DiscountAmount, retenue.Currency, retenue.ExpiresAtUtc);
    }

    public async Task<bool> CommitAsync(
        Guid reservationId, Guid orderId, CancellationToken cancellationToken = default)
        => (await _sender.Send(new CommitCouponCommand(reservationId, orderId), cancellationToken))
            .IsSuccess;

    public async Task<bool> ReleaseAsync(
        Guid reservationId, CancellationToken cancellationToken = default)
        => (await _sender.Send(new ReleaseCouponCommand(reservationId), cancellationToken))
            .IsSuccess;

    /// <summary>« FOOD » → <see cref="PromotionScope.Food"/>.</summary>
    private static PromotionScope Univers(string? scope) => scope?.Trim().ToUpperInvariant() switch
    {
        "FOOD" => PromotionScope.Food,
        "MARKETPLACE" => PromotionScope.Marketplace,
        _ => PromotionScope.Global
    };
}
