using FluentAssertions;
using HBA.Promotions.Domain.Promotions;
using Xunit;

namespace HBA.Promotions.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TROIS ANOMALIES DU LOT D28, ÉPROUVÉES SUR LE DOMAINE SEUL.
///
/// ISSUE-052 (qui paie la remise), ISSUE-053 (le budget d'une retenue expirée ne
/// revient jamais) et la moitié domaine d'ISSUE-033 (la décomposition que le
/// fournisseur de tarification consomme). Toutes sont des règles d'agrégat : elles
/// n'ont besoin ni de base, ni de serveur, ni de Kafka pour être fausses.
///
/// CE QUI N'EST PAS COUVERT ICI, ET IL FAUT LE SAVOIR.
///
///   • `PromotionPricingModuleApi` — la quote-part par ligne, l'imputation à la
///     PLATEFORME quand le vendeur de la ligne n'est pas le financeur, le repli
///     « promotion-service injoignable ». Il vit dans l'Infrastructure de
///     cart-service, que ce projet ne référence pas — et le référencer y ferait
///     entrer EF, MediatR et gRPC pour trois assertions.
///
///   • LA MIGRATION `20260901000100_FinanceurDePromotion`, et notamment le défaut
///     posé aux lignes existantes.
///
///   • LE CÂBLAGE du balayeur : qu'`ExpireCouponHoldsWorker` soit enregistré, que
///     sa période soit lue, que `ListWithExpiredHoldsAsync` se traduise en SQL.
///     Cela demande le service entier.
///
///   • LA GARDE D'APPARTENANCE de `/api/v1/merchant/promotions` : elle est dans
///     l'Api et demande un hôte. Les projets `*.AuthorizationTests` du dépôt sont
///     l'endroit pour cela, et promotion n'en a pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class FinanceurEtBalayageTests
{
    private static readonly DateTime Maintenant = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UnVendeur = Guid.NewGuid();

    private static Promotion Campagne(
        int partVendeurBps = PromotionFunding.PlatformOnly,
        Guid? proprietaire = null,
        long valeur = 10,
        long? budget = 100_000)
        => Promotion.Create(
            "Rentrée", PromotionScope.Global, PromotionType.Percent, valeur,
            Maintenant.AddDays(-1), Maintenant.AddDays(10), budget, "XOF",
            partVendeurBps, proprietaire).Value;

    /// <summary>
    /// Le calcul de wallet, recopié à l'identique : `AccrueEarningsOnOrderConfirmedHandler`
    /// fait `(UnitBasePrice - SellerDiscount) * Quantity`. C'est LUI qui prélève, et
    /// c'est donc lui qu'il faut simuler pour vérifier qu'on ne prélève pas à tort.
    /// </summary>
    private static long RevenuVendeur(long prixDeBase, long remiseVendeur)
        => Math.Max(0, prixDeBase - remiseVendeur);

    // ═════════════════════════════════════════════════════ ISSUE-052 · financeur

    /// <summary>
    /// LE DÉFAUT QUE D28 CORRIGE, DANS SA FORME LA PLUS DIRECTE.
    ///
    /// Brancher promotion-service sans financeur aurait fait supporter aux vendeurs
    /// les coupons de la plateforme — silencieusement, par le calcul des gains, et
    /// découvert au premier relevé contesté.
    /// </summary>
    [Fact]
    public void Une_remise_financee_par_la_plateforme_n_entame_pas_le_revenu_vendeur()
    {
        var campagne = Campagne();
        var remise = campagne.ComputeDiscount(new PromotionContext(
            PromotionScope.Marketplace, 10_000, 1_000, "XOF", Guid.NewGuid()));

        var imputation = campagne.SplitDiscount(remise.Total);

        imputation.SellerAmount.Should().Be(0);
        imputation.PlatformAmount.Should().Be(1_000);
        campagne.Funder.Should().Be(PromotionFunder.Platform);

        RevenuVendeur(10_000, imputation.SellerAmount).Should().Be(10_000);
    }

    [Fact]
    public void Une_remise_financee_par_le_vendeur_entame_son_revenu()
    {
        var campagne = Campagne(PromotionFunding.SellerOnly, UnVendeur);
        var remise = campagne.ComputeDiscount(new PromotionContext(
            PromotionScope.Marketplace, 10_000, 1_000, "XOF", Guid.NewGuid()));

        var imputation = campagne.SplitDiscount(remise.Total);

        imputation.SellerAmount.Should().Be(1_000);
        imputation.PlatformAmount.Should().Be(0);
        campagne.Funder.Should().Be(PromotionFunder.Seller);
        campagne.OwnerSellerId.Should().Be(UnVendeur);

        RevenuVendeur(10_000, imputation.SellerAmount).Should().Be(9_000);
    }

    /// <summary>
    /// C'EST L'EXIGENCE EXPLICITE DE D28 : « le champ doit permettre d'exprimer
    /// plus tard une remise COFINANCÉE sans migration supplémentaire ».
    ///
    /// Ce test est la preuve que la forme retenue — une part en points de base —
    /// le permet DÉJÀ. Un `funded_by` à deux valeurs aurait échoué ici, et la
    /// correction aurait été une seconde migration.
    /// </summary>
    [Fact]
    public void Une_remise_cofinancee_s_exprime_sans_colonne_supplementaire()
    {
        var campagne = Campagne(6_000, UnVendeur);

        campagne.Funder.Should().Be(PromotionFunder.Shared);

        var imputation = campagne.SplitDiscount(1_000);

        imputation.SellerAmount.Should().Be(600);
        imputation.PlatformAmount.Should().Be(400);
    }

    /// <summary>
    /// LA SOMME DES DEUX PARTS VAUT TOUJOURS EXACTEMENT LA REMISE, ET LE RESTE
    /// D'ARRONDI VA À LA PLATEFORME.
    ///
    /// 1 001 × 50 % vaut 500,5. En arithmétique entière il faut choisir qui absorbe
    /// le franc, et le choix n'est pas neutre : le faire porter au vendeur
    /// produirait, sur un relevé mensuel, des écarts d'un franc qu'aucune ligne
    /// n'explique. Un seul franc suffit à faire douter d'un relevé entier.
    /// </summary>
    [Fact]
    public void Le_reste_d_arrondi_est_supporte_par_la_plateforme()
    {
        var imputation = Campagne(5_000, UnVendeur).SplitDiscount(1_001);

        imputation.SellerAmount.Should().Be(500);
        imputation.PlatformAmount.Should().Be(501);
        imputation.Total.Should().Be(1_001);
    }

    /// <summary>
    /// UN PAYEUR SANS NOM EST REFUSÉ À LA CRÉATION.
    ///
    /// « Le vendeur paie » sans dire lequel obligerait le fournisseur de
    /// tarification à imputer à n'importe quel vendeur du panier — donc au mauvais.
    /// </summary>
    [Fact]
    public void Une_part_vendeur_sans_proprietaire_est_refusee()
    {
        var creation = Promotion.Create(
            "Sans payeur", PromotionScope.Global, PromotionType.Percent, 10,
            Maintenant, Maintenant.AddDays(1), 1_000, "XOF",
            PromotionFunding.SellerOnly, ownerSellerId: null);

        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("promotions.funding_owner_required");
    }

    [Fact]
    public void Une_part_hors_bornes_est_refusee()
    {
        var creation = Promotion.Create(
            "Absurde", PromotionScope.Global, PromotionType.Percent, 10,
            Maintenant, Maintenant.AddDays(1), 1_000, "XOF", 10_001, UnVendeur);

        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("promotions.funding_share_invalid");
    }

    /// <summary>
    /// Sans financeur désigné, la campagne est celle de la PLATEFORME — le même
    /// défaut que celui posé aux lignes existantes par la migration, et pour la même
    /// raison : on ne facture pas un marchand qui n'a rien signé.
    /// </summary>
    [Fact]
    public void Une_campagne_sans_financeur_designe_est_celle_de_la_plateforme()
    {
        var campagne = Campagne();

        campagne.SellerFundedShareBps.Should().Be(0);
        campagne.OwnerSellerId.Should().BeNull();
        campagne.Funder.Should().Be(PromotionFunder.Platform);
    }

    // ══════════════════════════════════════════════════════ ISSUE-053 · balayage

    private static (Coupon Coupon, CouponReservation Retenue) CouponRetenu(
        Guid campagneId, long remise = 1_500, int? perUser = null)
    {
        var coupon = Coupon.Create(campagneId, "RENTREE10", null, perUser).Value;
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), remise, Maintenant).Value;

        return (coupon, retenue);
    }

    /// <summary>
    /// LE CŒUR D'ISSUE-053. `ExpiresAtUtc` était écrite depuis la migration
    /// initiale et relue par PERSONNE : une campagne passait `Exhausted` sur des
    /// paniers que personne n'avait jamais payés.
    /// </summary>
    [Fact]
    public void Un_budget_reserve_puis_expire_redevient_disponible()
    {
        var campagne = Campagne(budget: 2_000);
        var (coupon, _) = CouponRetenu(campagne.Id);

        campagne.ConsumeBudget(1_500);
        campagne.BudgetRemaining.Should().Be(500);

        var bilan = coupon.ExpireHolds(Maintenant.Add(Coupon.HoldLifetime).AddMinutes(1));

        bilan.Count.Should().Be(1);
        bilan.Amount.Should().Be(1_500);

        campagne.ReleaseBudget(bilan.Amount);
        campagne.BudgetRemaining.Should().Be(2_000);
    }

    /// <summary>
    /// IDEMPOTENT, ET CE N'EST PAS UN LUXE : le balayeur repasse toutes les cinq
    /// minutes. Un second crédit à chaque tour ferait une campagne qui ne s'épuise
    /// jamais — c'est-à-dire l'inverse exact du défaut qu'on corrige, et tout aussi
    /// invisible.
    /// </summary>
    [Fact]
    public void Rejouer_le_balayage_ne_rend_pas_le_budget_deux_fois()
    {
        var campagne = Campagne(budget: 2_000);
        var (coupon, _) = CouponRetenu(campagne.Id);
        campagne.ConsumeBudget(1_500);

        var apres = Maintenant.Add(Coupon.HoldLifetime).AddMinutes(1);

        campagne.ReleaseBudget(coupon.ExpireHolds(apres).Amount);

        var second = coupon.ExpireHolds(apres);

        second.IsEmpty.Should().BeTrue();
        second.Amount.Should().Be(0);
        campagne.BudgetRemaining.Should().Be(2_000);
    }

    /// <summary>
    /// UN USAGE ENGAGÉ EST UNE VENTE PAYÉE : SON BUDGET EST DÛ.
    ///
    /// Le confondre avec une retenue abandonnée ferait de l'expiration un moyen
    /// d'effacer un usage payé — et rendrait à la campagne un budget qu'elle a
    /// réellement dépensé.
    /// </summary>
    [Fact]
    public void Un_usage_engage_n_est_jamais_balaye_meme_largement_expire()
    {
        var campagne = Campagne(budget: 2_000);
        var (coupon, retenue) = CouponRetenu(campagne.Id);

        coupon.Commit(retenue.Id, Guid.NewGuid(), Maintenant).IsSuccess.Should().BeTrue();

        coupon.ExpireHolds(Maintenant.AddDays(30)).IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// SANS CELA, UN SEUL PANIER ABANDONNÉ AU MAUVAIS MOMENT ÉTEINDRAIT
    /// DÉFINITIVEMENT UNE CAMPAGNE DONT LE BUDGET EST INTACT.
    /// </summary>
    [Fact]
    public void Une_campagne_epuisee_redevient_active_quand_le_budget_revient()
    {
        var campagne = Campagne(budget: 1_500);
        var (coupon, _) = CouponRetenu(campagne.Id);

        campagne.ConsumeBudget(1_500);
        campagne.Status.Should().Be(PromotionStatus.Exhausted);

        campagne.ReleaseBudget(
            coupon.ExpireHolds(Maintenant.Add(Coupon.HoldLifetime).AddMinutes(1)).Amount);

        campagne.Status.Should().Be(PromotionStatus.Active);
        campagne.BudgetRemaining.Should().Be(1_500);
    }

    // ═══════════════════════════════════════════════════ Plafond par acheteur

    /// <summary>
    /// LE PLAFOND PAR COMPTE SE COMPTE SUR LES USAGES ENGAGÉS **ET** RETENUS.
    ///
    /// Ne compter que les engagés laisserait un même compte ouvrir cent paniers et
    /// retenir cent fois le coupon avant d'en payer un seul : le budget global
    /// s'épuiserait sans qu'aucune limite individuelle ne soit dépassée.
    /// </summary>
    [Fact]
    public void Un_coupon_au_dela_du_plafond_par_acheteur_est_refuse()
    {
        var coupon = Coupon.Create(Guid.NewGuid(), "RENTREE10", null, perUserLimit: 1).Value;
        var acheteur = Guid.NewGuid();

        coupon.Reserve(acheteur, Guid.NewGuid(), 1_500, Maintenant).IsSuccess.Should().BeTrue();

        var seconde = coupon.Reserve(acheteur, Guid.NewGuid(), 1_500, Maintenant);

        seconde.IsFailure.Should().BeTrue();
        seconde.Error.Code.Should().Be("promotions.coupon.per_user_limit_reached");
    }

    /// <summary>
    /// ET IL SE ROUVRE QUAND LA RETENUE EXPIRE.
    ///
    /// Sinon un client qui abandonne un panier se verrouillerait lui-même sur un
    /// coupon qu'il n'a jamais utilisé — l'autre moitié d'ISSUE-053, celle qui, à
    /// l'inverse du budget, se répare toute seule à la lecture.
    /// </summary>
    [Fact]
    public void Le_plafond_par_acheteur_se_rouvre_a_l_expiration_de_la_retenue()
    {
        var coupon = Coupon.Create(Guid.NewGuid(), "RENTREE10", null, perUserLimit: 1).Value;
        var acheteur = Guid.NewGuid();

        coupon.Reserve(acheteur, Guid.NewGuid(), 1_500, Maintenant);

        var apres = Maintenant.Add(Coupon.HoldLifetime).AddMinutes(1);

        coupon.Reserve(acheteur, Guid.NewGuid(), 1_500, apres).IsSuccess.Should().BeTrue();
    }
}
