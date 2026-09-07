using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Application.Promotions;

/// <summary>Retenue accordée (§10.16, `ReserveCoupon`).</summary>
public sealed record CouponReservationView(
    Guid ReservationId, Guid CouponId, Guid PromotionId, long DiscountAmount,
    string Currency, DateTime ExpiresAtUtc);

// ══════════════════════════════════════════════════════════════════ Évaluation

/// <summary>
/// Valide un coupon pour un panier SANS RIEN CONSOMMER (§10.16, `POST
/// /api/v1/promotions/validate` et `EvaluatePromotion`).
/// </summary>
public sealed record ValidateCouponQuery(
    string? Code, PromotionScope Scope, long Subtotal, long DeliveryFee,
    string Currency, Guid UserId) : IQuery<PromotionEvaluation>;

internal sealed class ValidateCouponQueryHandler
    : IQueryHandler<ValidateCouponQuery, PromotionEvaluation>
{
    private readonly ICouponRepository _coupons;
    private readonly IPromotionRepository _promotions;

    public ValidateCouponQueryHandler(ICouponRepository coupons, IPromotionRepository promotions)
    {
        _coupons = coupons;
        _promotions = promotions;
    }

    public async Task<Result<PromotionEvaluation>> Handle(
        ValidateCouponQuery query, CancellationToken cancellationToken)
    {
        var contexte = new PromotionContext(
            query.Scope, query.Subtotal, query.DeliveryFee, query.Currency, query.UserId);

        var evaluation = await EvaluerAsync(_coupons, _promotions, query.Code, contexte, cancellationToken);

        // UN COUPON REFUSÉ N'EST PAS UNE ERREUR HTTP.
        return Result.Success(evaluation);
    }

    /// <summary>Le chemin d'évaluation, partagé entre la validation et la réservation.</summary>
    internal static async Task<PromotionEvaluation> EvaluerAsync(
        ICouponRepository coupons,
        IPromotionRepository promotions,
        string? code,
        PromotionContext contexte,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Refuse(contexte, "promotions.coupon.code_required", "Aucun code fourni.");
        }

        var coupon = await coupons.GetByCodeAsync(code.Trim().ToUpperInvariant(), cancellationToken);

        if (coupon is null)
        {
            // MÊME MESSAGE QU'UN COUPON EXPIRÉ, DÉLIBÉRÉMENT.
            return Refuse(contexte, "promotions.coupon.not_applicable", "Ce code n'est pas utilisable.");
        }

        var promotion = await promotions.GetByIdAsync(coupon.PromotionId, cancellationToken);

        if (promotion is null)
        {
            return Refuse(contexte, "promotions.coupon.not_applicable", "Ce code n'est pas utilisable.");
        }

        var applicable = promotion.EnsureApplicable(contexte, DateTime.UtcNow);

        if (applicable.IsFailure)
        {
            return Refuse(contexte, applicable.Error.Code, applicable.Error.Message, promotion.Id);
        }

        var remise = promotion.ComputeDiscount(contexte);

        if (remise.Total <= 0)
        {
            return Refuse(
                contexte, "promotions.no_discount",
                "Cette promotion ne donne aucune remise sur ce panier.", promotion.Id);
        }

        // LA RÉPARTITION EST DEMANDÉE AU DOMAINE, PAS RECALCULÉE ICI.
        var imputation = promotion.SplitDiscount(remise.Total);

        return new PromotionEvaluation(
            Valid: true,
            PromotionId: promotion.Id,
            Discount: remise.Total,
            Currency: promotion.Currency,
            Message: Libelle(promotion),
            Reason: null,
            SellerFundedDiscount: imputation.SellerAmount,
            PlatformFundedDiscount: imputation.PlatformAmount,
            OwnerSellerId: promotion.OwnerSellerId);
    }

    private static PromotionEvaluation Refuse(
        PromotionContext contexte, string raison, string message, Guid? promotionId = null)
        => new(false, promotionId, 0, contexte.Currency, message, raison);

    /// <summary>Le libellé affiché au client — « 10% de réduction » du §10.16.</summary>
    private static string Libelle(Promotion promotion) => promotion.Type switch
    {
        PromotionType.Percent => $"{promotion.Value}% de réduction",
        PromotionType.Fixed => $"{promotion.Value} {promotion.Currency} de réduction",
        PromotionType.FreeDelivery => "Livraison offerte",
        _ => promotion.Name
    };
}

// ═════════════════════════════════════════════════════════════════ Réservation

/// <summary>Retient un coupon pour un panier (§10.16, `ReserveCoupon`).</summary>
public sealed record ReserveCouponCommand(
    string? Code, Guid UserId, Guid CartId, PromotionScope Scope,
    long Subtotal, long DeliveryFee, string Currency = "XOF") : ICommand<CouponReservationView>;

internal sealed class ReserveCouponCommandHandler
    : ICommandHandler<ReserveCouponCommand, CouponReservationView>
{
    private readonly ICouponRepository _coupons;
    private readonly IPromotionRepository _promotions;
    private readonly IPromotionsUnitOfWork _unitOfWork;

    public ReserveCouponCommandHandler(
        ICouponRepository coupons, IPromotionRepository promotions, IPromotionsUnitOfWork unitOfWork)
    {
        _coupons = coupons;
        _promotions = promotions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CouponReservationView>> Handle(
        ReserveCouponCommand command, CancellationToken cancellationToken)
    {
        var contexte = new PromotionContext(
            command.Scope, command.Subtotal, command.DeliveryFee, command.Currency, command.UserId);

        var evaluation = await ValidateCouponQueryHandler.EvaluerAsync(
            _coupons, _promotions, command.Code, contexte, cancellationToken);

        if (!evaluation.Valid)
        {
            // ICI, EN REVANCHE, C'EST BIEN UNE ERREUR.
            return Result.Failure<CouponReservationView>(Error.BusinessRule(
                evaluation.Reason ?? "promotions.coupon.not_applicable", evaluation.Message));
        }

        var coupon = (await _coupons.GetByCodeAsync(
            command.Code!.Trim().ToUpperInvariant(), cancellationToken))!;

        var promotion = (await _promotions.GetByIdAsync(coupon.PromotionId, cancellationToken))!;

        // ON COMPTE LES RETENUES AVANT, ET CE N'EST PAS UN DÉTAIL.
        var avant = coupon.Reservations.Count;

        var retenue = coupon.Reserve(command.UserId, command.CartId, evaluation.Discount, DateTime.UtcNow);

        if (retenue.IsFailure)
        {
            return Result.Failure<CouponReservationView>(retenue.Error);
        }

        var estNeuve = coupon.Reservations.Count > avant;

        if (estNeuve)
        {
            var budget = promotion.ConsumeBudget(retenue.Value.DiscountAmount);

            if (budget.IsFailure)
            {
                return Result.Failure<CouponReservationView>(budget.Error);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CouponReservationView(
            retenue.Value.Id, coupon.Id, coupon.PromotionId,
            retenue.Value.DiscountAmount, promotion.Currency, retenue.Value.ExpiresAtUtc);
    }
}

// ═══════════════════════════════════════════════════════════════════ Engagement

/// <summary>Engage une retenue sur une commande payée (§10.16, `CommitCoupon`).</summary>
public sealed record CommitCouponCommand(Guid ReservationId, Guid OrderId) : ICommand;

internal sealed class CommitCouponCommandHandler : ICommandHandler<CommitCouponCommand>
{
    private readonly ICouponRepository _coupons;
    private readonly IPromotionsUnitOfWork _unitOfWork;

    public CommitCouponCommandHandler(ICouponRepository coupons, IPromotionsUnitOfWork unitOfWork)
    {
        _coupons = coupons;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CommitCouponCommand command, CancellationToken cancellationToken)
    {
        var coupon = await _coupons.GetByReservationAsync(command.ReservationId, cancellationToken);

        if (coupon is null)
        {
            return Result.Failure(Error.NotFound(
                ErrorCodes.NotFound(ServiceCodes.Promotion), "Réservation introuvable."));
        }

        var resultat = coupon.Commit(command.ReservationId, command.OrderId, DateTime.UtcNow);

        if (resultat.IsFailure)
        {
            return resultat;
        }

        // AUCUNE CONSOMMATION DE BUDGET ICI.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Libère une retenue : panier abandonné, ou checkout interrompu.</summary>
public sealed record ReleaseCouponCommand(Guid ReservationId) : ICommand;

internal sealed class ReleaseCouponCommandHandler : ICommandHandler<ReleaseCouponCommand>
{
    private readonly ICouponRepository _coupons;
    private readonly IPromotionRepository _promotions;
    private readonly IPromotionsUnitOfWork _unitOfWork;

    public ReleaseCouponCommandHandler(
        ICouponRepository coupons, IPromotionRepository promotions, IPromotionsUnitOfWork unitOfWork)
    {
        _coupons = coupons;
        _promotions = promotions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReleaseCouponCommand command, CancellationToken cancellationToken)
    {
        var coupon = await _coupons.GetByReservationAsync(command.ReservationId, cancellationToken);

        if (coupon is null)
        {
            return Result.Failure(Error.NotFound(
                ErrorCodes.NotFound(ServiceCodes.Promotion), "Réservation introuvable."));
        }

        var retenue = coupon.Reservations.FirstOrDefault(r => r.Id == command.ReservationId);

        if (retenue is null)
        {
            return Result.Failure(Error.NotFound(
                ErrorCodes.NotFound(ServiceCodes.Promotion), "Réservation introuvable."));
        }

        // LE MONTANT EST LU AVANT LA LIBÉRATION, ET SEULEMENT SI ELLE EST VIVANTE.
        var montant = retenue.Status == CouponReservationStatus.Held ? retenue.DiscountAmount : 0;

        var resultat = coupon.Release(command.ReservationId);

        if (resultat.IsFailure)
        {
            return resultat;
        }

        if (montant > 0)
        {
            var promotion = await _promotions.GetByIdAsync(coupon.PromotionId, cancellationToken);
            promotion?.ReleaseBudget(montant);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
