using FluentAssertions;
using HBA.Promotions.Domain.Promotions;
using Xunit;

namespace HBA.Promotions.Tests;

/// <summary>
/// Les coupons et leur réservation en deux temps (§10.16 : `ReserveCoupon` puis
/// `CommitCoupon`). C'est ici que se joue la différence entre « un coupon utilisé
/// une fois » et « un coupon utilisé cent fois par le même compte ».
/// </summary>
public sealed class CouponTests
{
    private static readonly DateTime Maintenant = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Campagne = Guid.NewGuid();

    private static Coupon UnCoupon(int? maxUses = null, int? perUser = null)
        => Coupon.Create(Campagne, "RENTREE10", maxUses, perUser).Value;

    [Fact]
    public void Le_code_est_normalise_en_majuscules()
    {
        Coupon.Create(Campagne, "  rentree10 ", null, null)
            .Value.Code.Should().Be("RENTREE10");
    }

    [Fact]
    public void Une_reservation_valide_est_retenue()
    {
        var reservation = UnCoupon().Reserve(Guid.NewGuid(), Guid.NewGuid(), 1_500, Maintenant);

        reservation.IsSuccess.Should().BeTrue();
        reservation.Value.Status.Should().Be(CouponReservationStatus.Held);
        reservation.Value.ExpiresAtUtc.Should().Be(Maintenant.Add(Coupon.HoldLifetime));
    }

    /// <summary>
    /// UN DOUBLE CLIC SUR « APPLIQUER » NE CONSOMME PAS DEUX USAGES.
    ///
    /// Sans cette règle, un client impatient épuise son propre plafond, et le budget
    /// se consomme deux fois pour un seul panier.
    /// </summary>
    [Fact]
    public void Reserver_deux_fois_pour_le_meme_panier_rend_la_meme_retenue()
    {
        var coupon = UnCoupon(perUser: 1);
        var utilisateur = Guid.NewGuid();
        var panier = Guid.NewGuid();

        var premiere = coupon.Reserve(utilisateur, panier, 1_500, Maintenant).Value;
        var seconde = coupon.Reserve(utilisateur, panier, 1_500, Maintenant);

        seconde.IsSuccess.Should().BeTrue();
        seconde.Value.Id.Should().Be(premiere.Id);
        coupon.CountUses(Maintenant).Should().Be(1);
    }

    [Fact]
    public void Le_plafond_global_d_usages_est_respecte()
    {
        var coupon = UnCoupon(maxUses: 2);
        coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant);
        coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant);

        var troisieme = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant);

        troisieme.IsFailure.Should().BeTrue();
        troisieme.Error.Code.Should().Be("promotions.coupon.max_uses_reached");
    }

    /// <summary>
    /// LE PLAFOND PAR COMPTE COMPTE AUSSI LES RETENUES, PAS SEULEMENT LES USAGES
    /// PAYÉS.
    ///
    /// Ne compter que les usages engagés laisserait un même compte ouvrir cent
    /// paniers et retenir cent fois le coupon avant d'en payer un seul : le budget
    /// global s'épuise sans qu'aucune limite individuelle ne soit dépassée.
    /// </summary>
    [Fact]
    public void Le_plafond_par_compte_compte_les_retenues_non_encore_payees()
    {
        var coupon = UnCoupon(perUser: 1);
        var utilisateur = Guid.NewGuid();

        coupon.Reserve(utilisateur, Guid.NewGuid(), 100, Maintenant);
        var seconde = coupon.Reserve(utilisateur, Guid.NewGuid(), 100, Maintenant);

        seconde.IsFailure.Should().BeTrue();
        seconde.Error.Code.Should().Be("promotions.coupon.per_user_limit_reached");
    }

    /// <summary>
    /// ET L'EXPIRATION RÉPARE LE CAS INVERSE.
    ///
    /// Sans elle, un client qui abandonne un panier se verrouillerait lui-même :
    /// sa retenue compterait pour toujours contre son propre plafond.
    /// </summary>
    [Fact]
    public void Une_retenue_expiree_cesse_de_compter_contre_le_plafond()
    {
        var coupon = UnCoupon(perUser: 1);
        var utilisateur = Guid.NewGuid();
        coupon.Reserve(utilisateur, Guid.NewGuid(), 100, Maintenant);

        var apres = Maintenant.Add(Coupon.HoldLifetime).AddMinutes(1);
        var seconde = coupon.Reserve(utilisateur, Guid.NewGuid(), 100, apres);

        seconde.IsSuccess.Should().BeTrue("une retenue abandonnée ne doit pas verrouiller son auteur");
        coupon.CountUsesBy(utilisateur, apres).Should().Be(1);
    }

    [Fact]
    public void Un_usage_engage_compte_pour_toujours()
    {
        var coupon = UnCoupon(perUser: 1);
        var utilisateur = Guid.NewGuid();
        var retenue = coupon.Reserve(utilisateur, Guid.NewGuid(), 100, Maintenant).Value;
        coupon.Commit(retenue.Id, Guid.NewGuid(), Maintenant);

        var tresTard = Maintenant.AddYears(1);

        coupon.CountUsesBy(utilisateur, tresTard).Should().Be(1);
        coupon.Reserve(utilisateur, Guid.NewGuid(), 100, tresTard).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// Kafka livre au moins une fois : engager deux fois la même retenue compterait
    /// deux usages pour une seule commande.
    /// </summary>
    [Fact]
    public void Engager_deux_fois_la_meme_retenue_est_sans_effet()
    {
        var coupon = UnCoupon(perUser: 5);
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant).Value;
        var commande = Guid.NewGuid();

        coupon.Commit(retenue.Id, commande, Maintenant).IsSuccess.Should().BeTrue();
        coupon.Commit(retenue.Id, commande, Maintenant).IsSuccess.Should().BeTrue();

        coupon.CountUses(Maintenant).Should().Be(1);
    }

    /// <summary>
    /// ON REFUSE D'ENGAGER UNE RETENUE EXPIRÉE PLUTÔT QUE DE LA PROLONGER.
    ///
    /// Le budget a pu être rendu et réattribué entre-temps : engager ici dépenserait
    /// deux fois la même enveloppe.
    /// </summary>
    [Fact]
    public void Une_retenue_expiree_ne_peut_plus_etre_engagee()
    {
        var coupon = UnCoupon();
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant).Value;

        var resultat = coupon.Commit(
            retenue.Id, Guid.NewGuid(),
            Maintenant.Add(Coupon.HoldLifetime).AddMinutes(1));

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.coupon.reservation_expired");
    }

    [Fact]
    public void Une_retenue_liberee_ne_peut_plus_etre_engagee()
    {
        var coupon = UnCoupon();
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant).Value;
        coupon.Release(retenue.Id);

        var resultat = coupon.Commit(retenue.Id, Guid.NewGuid(), Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.coupon.reservation_released");
    }

    [Fact]
    public void Liberer_une_retenue_la_retire_des_usages()
    {
        var coupon = UnCoupon(maxUses: 1);
        var retenue = coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant).Value;

        coupon.Release(retenue.Id);

        coupon.CountUses(Maintenant).Should().Be(0);
        coupon.Reserve(Guid.NewGuid(), Guid.NewGuid(), 100, Maintenant).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_retenue_inconnue_est_signalee_comme_introuvable()
    {
        UnCoupon().Commit(Guid.NewGuid(), Guid.NewGuid(), Maintenant)
            .Error.Code.Should().Be("promotions.coupon.reservation_not_found");
    }

    [Fact]
    public void Un_code_trop_court_ou_un_plafond_negatif_sont_refuses()
    {
        Coupon.Create(Campagne, "AB", null, null).IsFailure.Should().BeTrue();
        Coupon.Create(Campagne, "VALIDE", 0, null).IsFailure.Should().BeTrue();
    }
}
