using FluentAssertions;
using HBA.Promotions.Domain.Promotions;
using HBA.Promotions.Domain.Promotions.Events;
using HBA.Shared.Domain.Events;
using Xunit;

namespace HBA.Promotions.Tests;

/// <summary>
/// Les trois événements du §10.16 : <c>promotion.created</c>,
/// <c>promotion.exhausted</c>, <c>coupon.used</c>.
///
/// Ce qui se teste ici n'est pas qu'ils partent — c'est qu'ils ne partent PAS
/// deux fois. Un événement dupliqué ne casse rien visiblement : il fausse un
/// compteur, et personne ne remonte à sa source.
/// </summary>
public sealed class PromotionEventTests
{
    private static readonly DateTime Maintenant = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static Promotion Campagne(long? budget = 100_000)
        => Promotion.Create(
            "Rentrée", PromotionScope.Global, PromotionType.Percent, 10,
            Maintenant.AddDays(-1), Maintenant.AddDays(10), budget).Value;

    private static Coupon UnCoupon()
        => Coupon.Create(Guid.NewGuid(), "RENTREE10", null, null).Value;

    private static int Compte<T>(IHasDomainEvents agregat) where T : IDomainEvent
        => agregat.DomainEvents.OfType<T>().Count();

    // ────────────────────────────────────────────────────── promotion.created

    [Fact]
    public void Creer_une_campagne_leve_l_evenement_de_creation()
    {
        var campagne = Campagne();

        var evenement = campagne.DomainEvents.OfType<PromotionCreatedDomainEvent>().Single();

        evenement.PromotionId.Should().Be(campagne.Id);
        evenement.Scope.Should().Be("Global");
        evenement.Type.Should().Be("Percent");
        evenement.Currency.Should().Be("XOF");
    }

    /// <summary>
    /// Une campagne invalide n'existe pas, donc ne porte aucun événement : la
    /// validation passe AVANT la construction, pas après.
    /// </summary>
    [Fact]
    public void Une_campagne_invalide_n_est_jamais_construite()
    {
        var creation = Promotion.Create(
            "Absurde", PromotionScope.Global, PromotionType.Percent, 150,
            Maintenant, Maintenant.AddDays(1), 1_000);

        creation.IsFailure.Should().BeTrue();
        creation.Invoking(c => c.Value).Should().Throw<InvalidOperationException>();
    }

    // ──────────────────────────────────────────────────── promotion.exhausted

    [Fact]
    public void Epuiser_le_budget_leve_l_evenement_une_fois()
    {
        var campagne = Campagne(budget: 1_000);

        campagne.ConsumeBudget(1_000);

        Compte<PromotionExhaustedDomainEvent>(campagne).Should().Be(1);
    }

    /// <summary>
    /// LE TEST QUI EMPÊCHE L'ALERTE DE DEVENIR DU BRUIT.
    ///
    /// Ce n'est pas un cas limite, c'est le cas courant : une fois le budget
    /// épuisé, TOUTE tentative de réservation suivante retombe sur « budget
    /// insuffisant ». Une campagne populaire qui vient de s'épuiser reçoit des
    /// dizaines d'appels par minute — et publierait autant d'événements.
    /// </summary>
    [Fact]
    public void Les_tentatives_qui_suivent_l_epuisement_ne_reannoncent_pas()
    {
        var campagne = Campagne(budget: 1_000);
        campagne.ConsumeBudget(1_000);

        for (var i = 0; i < 20; i++)
        {
            campagne.ConsumeBudget(500).IsFailure.Should().BeTrue();
        }

        Compte<PromotionExhaustedDomainEvent>(campagne).Should().Be(1);
    }

    /// <summary>
    /// EN REVANCHE, UNE RÉOUVERTURE SUIVIE D'UN NOUVEL ÉPUISEMENT RÉ-ANNONCE.
    ///
    /// `ReleaseBudget` rend la campagne active — panier abandonné, commande
    /// annulée. Si elle s'épuise de nouveau, c'est un FAIT NOUVEAU : le budget
    /// rendu a été reconsommé. Le taire laisserait le marketing sur une
    /// information périmée. La garde vise les refus répétés, pas les transitions.
    /// </summary>
    [Fact]
    public void Une_campagne_reouverte_puis_reepuisee_annonce_de_nouveau()
    {
        var campagne = Campagne(budget: 1_000);

        campagne.ConsumeBudget(1_000);
        campagne.ReleaseBudget(400);
        campagne.Status.Should().Be(PromotionStatus.Active, "la libération rouvre la campagne");
        campagne.ConsumeBudget(400);

        campagne.Status.Should().Be(PromotionStatus.Exhausted);
        Compte<PromotionExhaustedDomainEvent>(campagne).Should().Be(2);
    }

    [Fact]
    public void Un_refus_pour_budget_insuffisant_epuise_et_annonce()
    {
        var campagne = Campagne(budget: 1_000);
        campagne.ConsumeBudget(700);

        campagne.ConsumeBudget(500).IsFailure.Should().BeTrue();

        Compte<PromotionExhaustedDomainEvent>(campagne).Should().Be(1);
    }

    [Fact]
    public void Une_campagne_sans_plafond_n_annonce_jamais_d_epuisement()
    {
        var campagne = Campagne(budget: null);

        campagne.ConsumeBudget(999_999_999);

        Compte<PromotionExhaustedDomainEvent>(campagne).Should().Be(0);
    }

    // ───────────────────────────────────────────────────────────── coupon.used

    [Fact]
    public void Engager_une_retenue_leve_coupon_used()
    {
        var coupon = UnCoupon();
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1_500, Maintenant).Value;
        var commande = Guid.NewGuid();

        coupon.Commit(retenue.Id, commande, Maintenant);

        var evenement = coupon.DomainEvents.OfType<CouponUsedDomainEvent>().Single();
        evenement.OrderId.Should().Be(commande);
        evenement.DiscountAmount.Should().Be(1_500);
        evenement.Code.Should().Be("RENTREE10");
    }

    /// <summary>
    /// KAFKA LIVRE AU MOINS UNE FOIS : LE REJEU EST LA NORME.
    ///
    /// `Commit` rend déjà `Success` sur un rejeu — c'est ce qui rend l'opération
    /// sûre. Mais publier depuis cette branche compterait un second usage pour une
    /// seule commande, et la remise annoncée au marketing ne correspondrait plus à
    /// aucune ligne comptable.
    /// </summary>
    [Fact]
    public void Un_rejeu_d_engagement_ne_leve_pas_un_second_coupon_used()
    {
        var coupon = UnCoupon();
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1_500, Maintenant).Value;
        var commande = Guid.NewGuid();

        coupon.Commit(retenue.Id, commande, Maintenant);
        coupon.Commit(retenue.Id, commande, Maintenant).IsSuccess.Should().BeTrue();

        Compte<CouponUsedDomainEvent>(coupon).Should().Be(1);
    }

    [Fact]
    public void Un_engagement_refuse_ne_leve_rien()
    {
        var coupon = UnCoupon();
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1_500, Maintenant).Value;
        coupon.Release(retenue.Id);

        coupon.Commit(retenue.Id, Guid.NewGuid(), Maintenant).IsFailure.Should().BeTrue();

        Compte<CouponUsedDomainEvent>(coupon).Should().Be(0);
    }

    // ──────────────────────────────────────────── Annulation de commande

    /// <summary>
    /// DEUX CHOSES À RENDRE, ET L'OUBLI DE L'UNE NE SE VOIT PAS.
    ///
    /// Le droit d'usage du client — sinon un acheteur dont la commande a été
    /// annulée reste bloqué sur son plafond pour une commande qu'il n'a jamais
    /// reçue. Et le montant à rendre au budget — sinon l'enveloppe se vide sur des
    /// commandes qui n'ont jamais existé.
    /// </summary>
    [Fact]
    public void Annuler_une_commande_rend_le_droit_d_usage_et_le_montant()
    {
        var coupon = Coupon.Create(Guid.NewGuid(), "RENTREE10", null, 1).Value;
        var utilisateur = Guid.NewGuid();
        var commande = Guid.NewGuid();

        var retenue = coupon.Reserve(utilisateur, Guid.NewGuid(), 1_500, Maintenant).Value;
        coupon.Commit(retenue.Id, commande, Maintenant);
        coupon.CountUsesBy(utilisateur, Maintenant).Should().Be(1);

        var aRendre = coupon.RevokeForCancelledOrder(commande);

        aRendre.Should().Be(1_500);
        coupon.CountUsesBy(utilisateur, Maintenant).Should().Be(0, "le client récupère son droit d'usage");
        coupon.Reserve(utilisateur, Guid.NewGuid(), 1_500, Maintenant).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Un rejeu de `marketplace.order.cancelled` ne doit rien recréditer : sinon le
    /// budget gonfle à chaque livraison, et la campagne ne s'épuise jamais.
    /// </summary>
    [Fact]
    public void Rejouer_l_annulation_ne_rend_rien_une_seconde_fois()
    {
        var coupon = UnCoupon();
        var commande = Guid.NewGuid();
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1_500, Maintenant).Value;
        coupon.Commit(retenue.Id, commande, Maintenant);

        coupon.RevokeForCancelledOrder(commande).Should().Be(1_500);
        coupon.RevokeForCancelledOrder(commande).Should().Be(0);
    }

    [Fact]
    public void Annuler_une_commande_sans_coupon_ne_rend_rien()
        => UnCoupon().RevokeForCancelledOrder(Guid.NewGuid()).Should().Be(0);

    /// <summary>
    /// UNE RETENUE NON ENGAGÉE N'EST PAS UN USAGE À RÉVOQUER.
    ///
    /// Elle n'appartient à aucune commande — son `OrderId` est nul. La confondre
    /// avec un usage engagé ferait rendre du budget deux fois : une fois par
    /// l'expiration de la retenue, une fois par l'annulation.
    /// </summary>
    [Fact]
    public void Une_retenue_jamais_engagee_n_est_pas_revoquee_par_une_annulation()
    {
        var coupon = UnCoupon();
        coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1_500, Maintenant);

        coupon.RevokeForCancelledOrder(Guid.NewGuid()).Should().Be(0);
        coupon.CountUses(Maintenant).Should().Be(1, "la retenue reste vivante");
    }
}
