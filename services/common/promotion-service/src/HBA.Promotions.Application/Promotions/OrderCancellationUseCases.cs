using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Application.Promotions;

/// <summary>LA COMMANDE EST ANNULÉE : ON REND LE COUPON ET LE BUDGET.</summary>
public sealed record ReleaseCouponsForCancelledOrderCommand(Guid OrderId) : ICommand;

internal sealed class ReleaseCouponsForCancelledOrderCommandHandler
    : ICommandHandler<ReleaseCouponsForCancelledOrderCommand>
{
    private readonly ICouponRepository _coupons;
    private readonly IPromotionRepository _promotions;
    private readonly IPromotionsUnitOfWork _unitOfWork;

    public ReleaseCouponsForCancelledOrderCommandHandler(
        ICouponRepository coupons, IPromotionRepository promotions, IPromotionsUnitOfWork unitOfWork)
    {
        _coupons = coupons;
        _promotions = promotions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        ReleaseCouponsForCancelledOrderCommand command, CancellationToken cancellationToken)
    {
        var coupons = await _coupons.ListByOrderAsync(command.OrderId, cancellationToken);

        // « AUCUN COUPON » EST UN SUCCÈS, PAS UNE ERREUR.
        if (coupons.Count == 0)
        {
            return Result.Success();
        }

        foreach (var coupon in coupons)
        {
            var aRendre = coupon.RevokeForCancelledOrder(command.OrderId);

            if (aRendre <= 0)
            {
                // Rejeu : les usages de cette commande sont déjà libérés.
                continue;
            }

            var promotion = await _promotions.GetByIdAsync(coupon.PromotionId, cancellationToken);

            // La campagne a pu être supprimée depuis.
            promotion?.ReleaseBudget(aRendre);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
