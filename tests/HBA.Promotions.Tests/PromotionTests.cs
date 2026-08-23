using FluentAssertions;
using HBA.Promotions.Domain.Promotions;
using Xunit;

namespace HBA.Promotions.Tests;

/// <summary>
/// Les règles du §10.16. Chacune protège de l'argent : une remise mal bornée, un
/// budget qui ne se referme pas, un coupon qui se réutilise.
/// </summary>
public sealed class PromotionTests
{
    private static readonly DateTime Maintenant = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static Promotion Campagne(
        PromotionType type = PromotionType.Percent,
        long valeur = 15,
        long? budget = 100_000,
        PromotionScope portee = PromotionScope.Global)
        => Promotion.Create(
            "Rentrée", portee, type, valeur,
            Maintenant.AddDays(-1), Maintenant.AddDays(10), budget).Value;

    private static PromotionContext Panier(
        long sousTotal = 10_000, long livraison = 1_000,
        PromotionScope portee = PromotionScope.Marketplace)
        => new(portee, sousTotal, livraison, "XOF", Guid.NewGuid());

    // ─────────────────────────────────────────────────────────── Calcul de remise

    [Fact]
    public void Une_remise_en_pourcentage_s_applique_au_sous_total()
    {
        var remise = Campagne(PromotionType.Percent, 15).ComputeDiscount(Panier(sousTotal: 10_000));

        remise.AmountOffSubtotal.Should().Be(1_500);
        remise.AmountOffDelivery.Should().Be(0);
    }

    /// <summary>
    /// LE TEST QUI EMPÊCHE DE RENDRE DE L'ARGENT À QUELQU'UN QUI N'A RIEN PAYÉ.
    ///
    /// Une remise fixe de 5 000 sur un panier de 3 000 donnerait un total de −2 000.
    /// Selon ce qu'en fait le service de paiement, c'est soit un échec, soit un
    /// remboursement. Les deux sont graves.
    /// </summary>
    [Fact]
    public void Une_remise_fixe_ne_depasse_jamais_le_sous_total()
    {
        var remise = Campagne(PromotionType.Fixed, 5_000).ComputeDiscount(Panier(sousTotal: 3_000));

        remise.AmountOffSubtotal.Should().Be(3_000);
    }

    [Fact]
    public void La_livraison_offerte_ne_touche_pas_au_sous_total()
    {
        var remise = Campagne(PromotionType.FreeDelivery, 1)
            .ComputeDiscount(Panier(sousTotal: 10_000, livraison: 1_500));

        remise.AmountOffSubtotal.Should().Be(0);
        remise.AmountOffDelivery.Should().Be(1_500);
    }

    [Fact]
    public void Une_remise_de_plus_de_cent_pour_cent_est_refusee_a_la_creation()
    {
        var resultat = Promotion.Create(
            "Absurde", PromotionScope.Global, PromotionType.Percent, 150,
            Maintenant, Maintenant.AddDays(1), 1_000);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.percent_above_hundred");
    }

    // ──────────────────────────────────────────────────────────── Applicabilité

    /// <summary>
    /// UNE FUITE DE BUDGET QUE PERSONNE NE REMARQUE AVANT LA CLÔTURE DU MOIS.
    ///
    /// Un coupon « −15 % sur les restaurants » appliqué à un panier Marketplace
    /// n'échoue nulle part : il remise simplement le mauvais univers.
    /// </summary>
    [Fact]
    public void Une_promotion_Food_ne_s_applique_pas_a_un_panier_Marketplace()
    {
        var resultat = Campagne(portee: PromotionScope.Food)
            .EnsureApplicable(Panier(portee: PromotionScope.Marketplace), Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.scope_mismatch");
    }

    [Fact]
    public void Une_promotion_globale_s_applique_aux_deux_univers()
    {
        var campagne = Campagne(portee: PromotionScope.Global);

        campagne.EnsureApplicable(Panier(portee: PromotionScope.Food), Maintenant)
            .IsSuccess.Should().BeTrue();
        campagne.EnsureApplicable(Panier(portee: PromotionScope.Marketplace), Maintenant)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_promotion_hors_fenetre_est_refusee_avec_la_bonne_raison()
    {
        var campagne = Campagne();

        campagne.EnsureApplicable(Panier(), Maintenant.AddDays(-5))
            .Error.Code.Should().Be("promotions.not_started");
        campagne.EnsureApplicable(Panier(), Maintenant.AddDays(30))
            .Error.Code.Should().Be("promotions.expired");
    }

    // ──────────────────────────────────────────────────────────────────── Budget

    /// <summary>
    /// ON NE SERT PAS UNE REMISE PARTIELLE.
    ///
    /// Accorder 300 quand il reste 300 sur une remise de 1 000 donnerait au client un
    /// montant qu'il n'a pas demandé, sans qu'aucun écran ne l'explique.
    /// </summary>
    [Fact]
    public void Un_budget_insuffisant_refuse_la_remise_entiere_et_epuise_la_campagne()
    {
        var campagne = Campagne(budget: 1_000);
        campagne.ConsumeBudget(700).IsSuccess.Should().BeTrue();

        var resultat = campagne.ConsumeBudget(500);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.exhausted");
        campagne.Status.Should().Be(PromotionStatus.Exhausted);
    }

    [Fact]
    public void Le_budget_exactement_consomme_epuise_la_campagne()
    {
        var campagne = Campagne(budget: 1_000);

        campagne.ConsumeBudget(1_000).IsSuccess.Should().BeTrue();
        campagne.Status.Should().Be(PromotionStatus.Exhausted);
        campagne.BudgetRemaining.Should().Be(0);
    }

    /// <summary>
    /// SANS CE COMPORTEMENT, UN PANIER ABANDONNÉ ÉTEINT UNE CAMPAGNE INTACTE.
    ///
    /// Le budget se consomme à la RÉSERVATION pour fermer la fenêtre de concurrence.
    /// La contrepartie est qu'un abandon immobilise du budget — et il faut donc que
    /// sa libération rouvre la campagne.
    /// </summary>
    [Fact]
    public void Rendre_du_budget_reouvre_une_campagne_epuisee()
    {
        var campagne = Campagne(budget: 1_000);
        campagne.ConsumeBudget(1_000);
        campagne.Status.Should().Be(PromotionStatus.Exhausted);

        campagne.ReleaseBudget(400);

        campagne.Status.Should().Be(PromotionStatus.Active);
        campagne.BudgetRemaining.Should().Be(400);
    }

    [Fact]
    public void Une_campagne_sans_budget_ne_s_epuise_pas()
    {
        var campagne = Campagne(budget: null);

        campagne.ConsumeBudget(999_999_999).IsSuccess.Should().BeTrue();
        campagne.Status.Should().NotBe(PromotionStatus.Exhausted);
    }

    [Fact]
    public void Rendre_plus_que_consomme_ne_rend_pas_le_budget_negatif()
    {
        var campagne = Campagne(budget: 1_000);
        campagne.ConsumeBudget(200);

        campagne.ReleaseBudget(900);

        campagne.BudgetConsumed.Should().Be(0);
    }

    [Fact]
    public void Une_campagne_epuisee_est_refusee_a_l_evaluation()
    {
        var campagne = Campagne(budget: 500);
        campagne.ConsumeBudget(500);

        campagne.EnsureApplicable(Panier(), Maintenant).Error.Code.Should().Be("promotions.exhausted");
    }
}
