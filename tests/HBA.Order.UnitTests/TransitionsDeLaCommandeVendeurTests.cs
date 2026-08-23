using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Order.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-026 — les cinq permissions gardent enfin quelque chose.
///
/// `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY` et
/// `ORDER_CANCEL` étaient distribuées au rôle `ORDER_MANAGER` sans garder aucune
/// route, faute d'objet à faire changer d'état. Ces tests éprouvent les
/// transitions que ces permissions autorisent désormais — et surtout celles
/// qu'elles n'autorisent PAS.
///
/// CE QU'ILS NE COUVRENT PAS : les permissions elles-mêmes et les gardes
/// d'appartenance, qui vivent au bord HTTP et relèvent de
/// `HBA.Order.AuthorizationTests`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TransitionsDeLaCommandeVendeurTests
{
    private static SellerOrder UnePartVendeur(Guid? sellerId = null)
    {
        var vendeur = sellerId ?? Guid.NewGuid();
        var commande = UneCommande.Confirmee(UneCommande.Marchandise(vendeur, quantite: 2));
        return SellerOrder.SplitFrom(commande, UneCommande.Maintenant).Value.Single();
    }

    [Fact]
    public void Le_chemin_nominal_va_de_l_attente_a_la_remise_au_livreur()
    {
        var part = UnePartVendeur();
        var t0 = UneCommande.Maintenant;

        part.Status.Should().Be(SellerOrderStatus.AwaitingConfirmation);

        part.Confirm(t0.AddMinutes(1)).IsSuccess.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.Confirmed);

        part.MarkPreparing(t0.AddMinutes(2)).IsSuccess.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.Preparing);

        part.MarkReadyForPickup(t0.AddMinutes(3)).IsSuccess.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.ReadyForPickup);

        part.MarkHandedOver(t0.AddMinutes(4)).IsSuccess.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.HandedOver);
        part.IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// UN HORODATAGE PAR ÉTAPE, ET IL EST POSÉ À L'ENTRÉE DANS L'ÉTAT.
    ///
    /// C'est ce qui permet de trier « ce vendeur a accepté il y a trois jours et
    /// n'a rien emballé ». Un champ unique « dernière transition » ne répondrait
    /// pas à cette question — c'est le même raisonnement que
    /// `Order.UnderReviewSinceUtc`.
    /// </summary>
    [Fact]
    public void Chaque_etape_pose_son_propre_horodatage()
    {
        var part = UnePartVendeur();
        var t0 = UneCommande.Maintenant;

        part.ConfirmedAtUtc.Should().BeNull("nul veut dire « ce n'est pas arrivé »");

        part.Confirm(t0.AddMinutes(1));
        part.MarkPreparing(t0.AddMinutes(2));
        part.MarkReadyForPickup(t0.AddMinutes(3));

        part.ConfirmedAtUtc.Should().Be(t0.AddMinutes(1));
        part.PreparingAtUtc.Should().Be(t0.AddMinutes(2));
        part.ReadyForPickupAtUtc.Should().Be(t0.AddMinutes(3));
        part.HandedOverAtUtc.Should().BeNull();
        part.RefusedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Une_part_non_confirmee_ne_passe_pas_en_preparation()
    {
        var part = UnePartVendeur();

        var resultat = part.MarkPreparing(UneCommande.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.seller_order.invalid_transition");
        part.Status.Should().Be(SellerOrderStatus.AwaitingConfirmation, "un refus ne mute rien");
    }

    /// <summary>
    /// LE SAUT « CONFIRMÉE → PRÊTE » EST REFUSÉ, ET C'EST DISCUTÉ.
    ///
    /// L'autoriser rendrait `Preparing` facultatif, donc absent de la moitié des
    /// commandes — et un état qu'on peut sauter ne dit plus rien à celui qui le
    /// lit. Or c'est exactement ce que l'exploitation regarde pour distinguer
    /// « accepté et en cours » de « accepté et oublié ». Voir
    /// `SellerOrder.MarkReadyForPickup` pour le coût assumé : un clic de plus.
    /// </summary>
    [Fact]
    public void Une_part_confirmee_ne_saute_pas_directement_a_prete()
    {
        var part = UnePartVendeur();
        part.Confirm(UneCommande.Maintenant);

        var resultat = part.MarkReadyForPickup(UneCommande.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.seller_order.invalid_transition");
        part.Status.Should().Be(SellerOrderStatus.Confirmed);
    }

    [Fact]
    public void Une_part_en_preparation_ne_se_remet_pas_au_livreur()
    {
        var part = UnePartVendeur();
        part.Confirm(UneCommande.Maintenant);
        part.MarkPreparing(UneCommande.Maintenant);

        part.MarkHandedOver(UneCommande.Maintenant).IsFailure.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.Preparing);
    }

    [Fact]
    public void Une_part_deja_confirmee_ne_se_confirme_pas_deux_fois()
    {
        var part = UnePartVendeur();
        part.Confirm(UneCommande.Maintenant).IsSuccess.Should().BeTrue();

        var seconde = part.Confirm(UneCommande.Maintenant.AddHours(1));

        seconde.IsFailure.Should().BeTrue();
        seconde.Error.Code.Should().Be("ordering.seller_order.invalid_transition");

        // L'HORODATAGE D'ORIGINE N'EST PAS ÉCRASÉ : un refus ne mute rien, et
        // « depuis quand ce vendeur s'est-il engagé » doit rester vrai.
        part.ConfirmedAtUtc.Should().Be(UneCommande.Maintenant);
    }

    [Fact]
    public void Une_part_remise_au_livreur_est_close()
    {
        var part = UnePartVendeur();
        part.Confirm(UneCommande.Maintenant);
        part.MarkPreparing(UneCommande.Maintenant);
        part.MarkReadyForPickup(UneCommande.Maintenant);
        part.MarkHandedOver(UneCommande.Maintenant);

        part.MarkPreparing(UneCommande.Maintenant).IsFailure.Should().BeTrue();
        part.Confirm(UneCommande.Maintenant).IsFailure.Should().BeTrue();
    }
}
