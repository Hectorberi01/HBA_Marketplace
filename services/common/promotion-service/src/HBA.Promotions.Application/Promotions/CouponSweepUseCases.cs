using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Application.Promotions;

/// <summary>Ce qu'un tour de balayage a rendu au budget des campagnes.</summary>
/// <param name="Coupons">Coupons touchés.</param>
/// <param name="Reservations">Retenues passées en <c>Released</c>.</param>
/// <param name="Budget">
/// Budget rendu, en unités monétaires entières (§2) — le VOLUME que l'audit
/// réclame nommément. Le nombre de lignes ne dit pas combien d'enveloppe dormait.
/// </param>
public sealed record CouponHoldSweepReport(int Coupons, int Reservations, long Budget)
{
    public static readonly CouponHoldSweepReport Empty = new(0, 0, 0);

    public bool IsEmpty => Reservations == 0;
}

/// <summary>
/// Rend au budget des campagnes les retenues de coupon dont l'échéance est passée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE BALAYAGE N'EXISTAIT PAS, ET LES CAMPAGNES S'ÉTEIGNAIENT SUR DES PANIERS
/// ABANDONNÉS (ISSUE-053).
///
/// `Coupon.HoldLifetime` vaut trente minutes. `CouponReservation.ExpiresAtUtc`
/// était écrite à chaque retenue, l'index partiel `ix_coupon_usages_expiring`
/// était posé « pour le ménage des retenues expirées »… et aucun `BackgroundService`
/// n'existait dans promotion-service. L'échéance valait donc trente minutes sur le
/// papier et l'INFINI en pratique.
///
/// Deux dégâts, dont un seul se plaint :
///
///   • le BUDGET de la campagne ne revient jamais. `Promotion.ConsumeBudget` l'a
///     débité à la réservation — c'est voulu, c'est ce qui ferme la fenêtre des
///     mille paniers simultanés — et seule une libération le rend. Une campagne
///     passe donc `Exhausted` sur des paniers que personne n'a payés, et
///     `promotion.exhausted` part vers le marketing avec un budget intact.
///
///   • le PLAFOND PAR COMPTE se referme sur le client. `CouponReservation.CountsAt`
///     cesse de compter une retenue expirée — cette moitié-là se réparait donc
///     toute seule, à la lecture. C'est ce qui rend le premier dégât invisible :
///     le client, lui, ne se plaint de rien.
///
/// IDEMPOTENT. `Coupon.ExpireHolds` ne voit que les retenues `Held` dont
/// l'échéance est passée ; une retenue libérée n'est plus `Held`. Rejouer le lot,
/// ou l'interrompre au milieu, ne recrédite jamais deux fois le même budget.
///
/// L'HORLOGE EST LUE UNE SEULE FOIS PAR LOT.
///
/// Sinon deux coupons du même tour seraient jugés à des instants différents, et
/// une retenue à la seconde près basculerait selon l'ordre de la boucle. Le module
/// n'a pas d'abstraction d'horloge — `ReserveCouponCommandHandler` lit
/// `DateTime.UtcNow` directement — et en introduire une pour un seul appelant
/// ajouterait une pièce que rien d'autre n'utilise.
///
/// CE QU'IL NE FAIT PAS : IL NE RÉOUVRE PAS `promotion.exhausted`.
///
/// `Promotion.ReleaseBudget` repasse la campagne en `Active` si du budget revient,
/// mais aucun événement ne l'annonce. Le marketing a reçu « épuisée » et ne
/// recevra pas « et finalement non ». C'est une limite connue, pas un oubli : un
/// événement de dés-épuisement se répéterait à chaque va-et-vient d'une campagne
/// au bord de son budget, et noierait l'alerte qu'il est censé corriger.
/// ═════════════════════════════════════════════════════════════════════════════
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
        //
        // Deux coupons du même lot peuvent appartenir à la même campagne. La
        // charger deux fois ne serait pas faux — EF rendrait la même instance
        // suivie — mais l'agrégation dit ce qu'on fait, et elle reste juste si le
        // dépôt cesse un jour de garder ses entités en suivi.
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

            // La campagne a pu être supprimée depuis. Les retenues sont libérées
            // quand même — c'est le droit d'usage du client, et il lui est dû ; le
            // budget d'une campagne qui n'existe plus n'intéresse personne. Ce
            // montant-là n'est donc pas compté comme « rendu », sinon le journal
            // annoncerait une enveloppe que personne ne peut dépenser.
            if (promotion is null)
            {
                continue;
            }

            promotion.ReleaseBudget(montant);
            budgetRendu += montant;
        }

        // UN SEUL `SaveChanges` POUR LE LOT, ET AUCUN SI RIEN N'A CHANGÉ.
        //
        // Le contexte dispatche les événements de domaine et draine l'outbox à
        // chaque sauvegarde : une sauvegarde à vide coûterait un aller-retour pour
        // zéro ligne, toutes les cinq minutes, à jamais.
        if (retenues > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new CouponHoldSweepReport(couponsTouches, retenues, budgetRendu);
    }
}
