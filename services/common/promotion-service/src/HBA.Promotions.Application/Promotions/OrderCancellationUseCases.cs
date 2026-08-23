using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Application.Promotions;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA COMMANDE EST ANNULÉE : ON REND LE COUPON ET LE BUDGET.
///
/// C'est la compensation des deux consommateurs du §10.16 —
/// `marketplace.order.cancelled` et `food.order.cancelled`. Une seule commande
/// pour les deux : du point de vue de la promotion, l'univers d'où vient
/// l'annulation ne change rien à ce qu'il faut défaire.
///
/// DEUX CHOSES À RENDRE, ET OUBLIER L'UNE NE SE VOIT PAS.
///
///   • le DROIT D'USAGE du client — sans quoi un acheteur dont la commande a été
///     annulée par le vendeur reste bloqué sur son plafond, pour une commande
///     qu'il n'a jamais reçue ;
///   • le BUDGET de la campagne — sans quoi l'enveloppe se vide sur des commandes
///     qui n'ont jamais existé, et la campagne s'éteint avant l'heure.
///
/// Le premier oubli produit une réclamation client ; le second ne produit rien du
/// tout, et c'est celui qui coûte le plus longtemps.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        //
        // L'écrasante majorité des commandes n'en porte aucun. Rendre un échec
        // ferait échouer le consommateur sur le cas NORMAL, et la reprise bornée
        // finirait par mettre en lettre morte des annulations parfaitement traitées.
        if (coupons.Count == 0)
        {
            return Result.Success();
        }

        foreach (var coupon in coupons)
        {
            var aRendre = coupon.RevokeForCancelledOrder(command.OrderId);

            if (aRendre <= 0)
            {
                // Rejeu : les usages de cette commande sont déjà libérés. Recréditer
                // ici gonflerait le budget à chaque livraison Kafka.
                continue;
            }

            var promotion = await _promotions.GetByIdAsync(coupon.PromotionId, cancellationToken);

            // La campagne a pu être supprimée depuis. Le droit d'usage du client est
            // rendu quand même — c'est lui qu'on lui doit ; le budget d'une campagne
            // qui n'existe plus n'intéresse personne.
            promotion?.ReleaseBudget(aRendre);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
