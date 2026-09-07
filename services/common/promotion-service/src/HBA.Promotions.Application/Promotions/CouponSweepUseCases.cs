using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Application.Promotions;

/// <summary>Ce qu'un tour de balayage a rendu au budget des campagnes.</summary>
/// <param name="Coupons">Coupons touchés.</param>
/// <param name="Reservations">Retenues passées en <c>Released</c>.</param>
/// <param name="Budget">
/// Budget rendu, en unités monétaires entières (§2) — le VOLUME que l'audit réclame
/// nommément.
/// </param>
public sealed record CouponHoldSweepReport(int Coupons, int Reservations, long Budget)
{
    public static readonly CouponHoldSweepReport Empty = new(0, 0, 0);

    public bool IsEmpty => Reservations == 0;
}

/// <summary>
/// Rend au budget des campagnes les retenues de coupon dont l'échéance est passée.
/// </summary>
public sealed record ExpireCouponHoldsCommand(int BatchSize = 100)
    : ICommand<CouponHoldSweepReport>;

internal sealed class ExpireCouponHoldsCommandHandler
    : ICommandHandler<ExpireCouponHoldsCommand, CouponHoldSweepReport>
{
    private readonly ICouponRepository _coupons;
    private readonly IPromotionRepository _promotions;
    private readonly IPromotionsUnitOfWork _unitOfWork;

    public ExpireCouponHoldsCommandHandler(
        ICouponRepository coupons, IPromotionRepository promotions, IPromotionsUnitOfWork unitOfWork)
    {
        _coupons = coupons;
        _promotions = promotions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CouponHoldSweepReport>> Handle(
        ExpireCouponHoldsCommand command, CancellationToken cancellationToken)
    {
        var maintenant = DateTime.UtcNow;

        var coupons = await _coupons.ListWithExpiredHoldsAsync(
            maintenant, command.BatchSize, cancellationToken);

        if (coupons.Count == 0)
        {
            return CouponHoldSweepReport.Empty;
        }

        var couponsTouches = 0;
        var retenues = 0;

        // ON CUMULE PAR CAMPAGNE AVANT DE CRÉDITER.
        var parCampagne = new Dictionary<Guid, long>();

        foreach (var coupon in coupons)
        {
            var bilan = coupon.ExpireHolds(maintenant);

            if (bilan.IsEmpty)
            {
                // La sélection est une requête, pas un verrou : une retenue a pu
                // être engagée ou libérée entre le SELECT et ici.
                continue;
            }

            couponsTouches++;
            retenues += bilan.Count;

            parCampagne[coupon.PromotionId] =
                parCampagne.TryGetValue(coupon.PromotionId, out var cumul) ? cumul + bilan.Amount : bilan.Amount;
        }

        var budgetRendu = 0L;

        foreach (var (promotionId, montant) in parCampagne)
        {
            if (montant <= 0)
            {
                continue;
            }

            var promotion = await _promotions.GetByIdAsync(promotionId, cancellationToken);

            // La campagne a pu être supprimée depuis.
            if (promotion is null)
            {
                continue;
            }

            promotion.ReleaseBudget(montant);
            budgetRendu += montant;
        }

        // UN SEUL `SaveChanges` POUR LE LOT, ET AUCUN SI RIEN N'A CHANGÉ.
        if (retenues > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new CouponHoldSweepReport(couponsTouches, retenues, budgetRendu);
    }
}
