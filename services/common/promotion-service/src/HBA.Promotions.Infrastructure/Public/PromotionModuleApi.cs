using HBA.Promotions.Application.Promotions;
using HBA.Promotions.Contracts;
using HBA.Promotions.Domain.Promotions;
using MediatR;

namespace HBA.Promotions.Infrastructure.Public;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'IMPLÉMENTATION DE <see cref="IPromotionModuleApi"/>.
///
/// ELLE NE FAIT QUE TRADUIRE. AUCUNE RÈGLE MÉTIER ICI.
///
/// Chaque méthode convertit le contrat public en commande MediatR et retraduit le
/// résultat. Toute décision — éligibilité, budget, plafonds — vit dans le domaine,
/// et c'est ce qui garantit que l'appel gRPC et la route REST rendent le MÊME
/// verdict. Une vérification recopiée ici aurait divergé au premier ajout de
/// condition, et le panier validé à l'écran aurait été refusé au checkout.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        //
        // La requête rend un succès même pour un coupon refusé — c'est voulu. Si
        // elle échoue quand même, la cause est ailleurs : base injoignable, panne
        // interne. Répondre « ce code n'est pas utilisable » ferait retirer au
        // client une remise à laquelle il a droit, sans trace.
        if (resultat.IsFailure)
        {
            return new PromotionEvaluationResult(
                false, null, 0, context.Currency,
                "Le service de promotion est momentanément indisponible.",
                resultat.Error.Code);
        }

        var evaluation = resultat.Value;

        // LA DÉCOMPOSITION PAR FINANCEUR TRAVERSE TELLE QUELLE (D28).
        //
        // Rien n'est recalculé ici : `Promotion.SplitDiscount` a déjà décidé, y
        // compris du sens de l'arrondi. Refaire la division à partir d'une part en
        // pourcentage donnerait, une fois sur deux, un franc de plus au vendeur —
        // et l'écart n'apparaîtrait que sur un relevé mensuel, sans ligne pour
        // l'expliquer.
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

    /// <summary>
    /// « FOOD » → <see cref="PromotionScope.Food"/>.
    ///
    /// UN UNIVERS INCONNU DEVIENT « GLOBAL », ET C'EST LE CHOIX SÛR ICI.
    ///
    /// Contrairement à une RÈGLE inconnue — que l'on refuse, parce qu'ignorer une
    /// restriction accorde la remise qu'elle interdisait — un scope inconnu rend
    /// l'évaluation PLUS stricte : seules les campagnes globales passeront, et une
    /// campagne ciblée sera écartée par `EnsureApplicable`. Lever aurait transformé
    /// une faute de frappe de l'appelant en panne de checkout.
    /// </summary>
    private static PromotionScope Univers(string? scope) => scope?.Trim().ToUpperInvariant() switch
    {
        "FOOD" => PromotionScope.Food,
        "MARKETPLACE" => PromotionScope.Marketplace,
        _ => PromotionScope.Global
    };
}
