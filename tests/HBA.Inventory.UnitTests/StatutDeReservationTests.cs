using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-045 — « `StockReservation` n'a aucun statut ».
///
/// `ReleaseReservation` et `ConfirmReservation` SUPPRIMAIENT les lignes. Une
/// vente confirmée devenait indiscernable d'une réservation inexistante, et rien
/// n'empêchait de « libérer » du stock déjà vendu et déjà décrémenté — c'est le
/// danger que l'audit nomme sur `POST /api/inventory/reservations/release` :
/// rendre à la vente une marchandise qui n'est plus là, donc la vendre deux fois.
///
/// Chaque test vérifie aussi `Reserved` ET `Available` : c'est le point où une
/// erreur ferait disparaître du stock en silence.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class StatutDeReservationTests
{
    [Fact]
    public void Une_reservation_nait_active()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();

        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure);

        var reservation = article.Reservation(commande);
        reservation.Status.Should().Be(ReservationStatus.Active);
        reservation.ConfirmedAtUtc.Should().BeNull();
        reservation.ReleasedAtUtc.Should().BeNull();
        reservation.ExpiredAtUtc.Should().BeNull();
        article.Reserved.Should().Be(3);
        article.Available.Should().Be(7);
    }

    /// <summary>
    /// La confirmation décrémente `OnHand` et solde la réservation. `Available`
    /// est INCHANGÉ : la marchandise n'était déjà plus vendable depuis qu'elle
    /// était réservée.
    /// </summary>
    [Fact]
    public void Une_confirmation_decremente_le_stock_physique_et_marque_la_ligne()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();
        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure);

        var confirmation = article.ConfirmReservation(commande, UnArticleDeStock.Maintenant);

        confirmation.IsSuccess.Should().BeTrue();
        article.OnHand.Should().Be(7);
        article.Reserved.Should().Be(0);
        article.Available.Should().Be(7);

        var reservation = article.Reservation(commande);
        reservation.Status.Should().Be(ReservationStatus.Confirmed);
        reservation.ConfirmedAtUtc.Should().Be(UnArticleDeStock.Maintenant);
        reservation.Quantity.Should().Be(3, "la ligne garde ce qui a été vendu : c'est l'historique");
    }

    /// <summary>
    /// LE TEST CENTRAL D'ISSUE-045. Une vente confirmée ne se relâche pas.
    /// Si `ReleaseReservation` rendait ces 3 unités, l'article afficherait 10
    /// disponibles alors qu'il n'en reste que 7 : la plateforme vendrait deux fois
    /// la même marchandise.
    /// </summary>
    [Fact]
    public void Une_reservation_confirmee_n_est_jamais_liberee()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();
        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure);
        article.ConfirmReservation(commande, UnArticleDeStock.Maintenant);

        var liberation = article.ReleaseReservation(commande, UnArticleDeStock.Maintenant.AddMinutes(5));

        liberation.IsSuccess.Should().BeTrue("une compensation ne doit pas échouer là où il n'y a rien à rendre");
        article.OnHand.Should().Be(7);
        article.Reserved.Should().Be(0);
        article.Available.Should().Be(7);

        var reservation = article.Reservation(commande);
        reservation.Status.Should().Be(ReservationStatus.Confirmed);
        reservation.ReleasedAtUtc.Should().BeNull("la ligne vendue ne doit porter aucune trace de libération");
    }

    /// <summary>
    /// Confirmer deux fois — webhook de PSP rejoué, reprise de saga — ne doit pas
    /// décrémenter `OnHand` une seconde fois. Avant, la ligne ayant été supprimée,
    /// le second appel rendait `NotFound` : l'appelant lisait « échec » sur une
    /// vente parfaitement faite.
    /// </summary>
    [Fact]
    public void Une_confirmation_rejouee_ne_decremente_pas_deux_fois()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();
        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure);
        article.ConfirmReservation(commande, UnArticleDeStock.Maintenant);

        var rejeu = article.ConfirmReservation(commande, UnArticleDeStock.Maintenant.AddMinutes(1));

        rejeu.IsSuccess.Should().BeTrue();
        article.OnHand.Should().Be(7);
        article.Available.Should().Be(7);
        article.Reservations.Should().HaveCount(1);
    }

    [Fact]
    public void Confirmer_une_commande_inconnue_reste_un_NotFound()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);

        var resultat = article.ConfirmReservation(Guid.NewGuid(), UnArticleDeStock.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("inventory.item.reservation_not_found");
        article.OnHand.Should().Be(10);
    }

    [Fact]
    public void Une_liberation_rend_le_stock_et_conserve_la_ligne()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();
        article.Reserve(commande, 4, UnArticleDeStock.DansUnQuartDHeure);

        article.ReleaseReservation(commande, UnArticleDeStock.Maintenant).IsSuccess.Should().BeTrue();

        article.OnHand.Should().Be(10);
        article.Reserved.Should().Be(0);
        article.Available.Should().Be(10);

        var reservation = article.Reservation(commande);
        reservation.Status.Should().Be(ReservationStatus.Released);
        reservation.ReleasedAtUtc.Should().Be(UnArticleDeStock.Maintenant);
    }

    /// <summary>
    /// LA RÉGRESSION LA PLUS COÛTEUSE POSSIBLE. Puisqu'on ne supprime plus les
    /// lignes, une somme naïve sur `Reservations` compterait les libérées, les
    /// expirées et surtout les CONFIRMÉES — dont le stock a déjà quitté `OnHand`.
    /// `Available` plongerait sous zéro et tout le stock vendable disparaîtrait.
    /// </summary>
    [Fact]
    public void Seules_les_reservations_actives_comptent_dans_Reserved()
    {
        var article = UnArticleDeStock.Avec(onHand: 20);
        var vendue = Guid.NewGuid();
        var annulee = Guid.NewGuid();
        var enCours = Guid.NewGuid();

        article.Reserve(vendue, 5, UnArticleDeStock.DansUnQuartDHeure);
        article.ConfirmReservation(vendue, UnArticleDeStock.Maintenant);

        article.Reserve(annulee, 4, UnArticleDeStock.DansUnQuartDHeure);
        article.ReleaseReservation(annulee, UnArticleDeStock.Maintenant);

        article.Reserve(enCours, 3, UnArticleDeStock.DansUnQuartDHeure);

        article.Reservations.Should().HaveCount(3, "aucune ligne n'est jamais supprimée");
        article.OnHand.Should().Be(15);
        article.Reserved.Should().Be(3);
        article.Available.Should().Be(12);
    }

    /// <summary>
    /// `AdjustOnHand` refuse de passer le stock sous le RÉSERVÉ. Ce plancher se
    /// lit sur `Reserved`, donc sur les seules réservations actives : une
    /// confirmation passée ne doit pas bloquer un inventaire physique.
    /// </summary>
    [Fact]
    public void Un_ajustement_se_compare_au_reserve_actif_seulement()
    {
        var article = UnArticleDeStock.Avec(onHand: 20);
        var vendue = Guid.NewGuid();
        article.Reserve(vendue, 8, UnArticleDeStock.DansUnQuartDHeure);
        article.ConfirmReservation(vendue, UnArticleDeStock.Maintenant);

        // OnHand = 12, Reserved = 0 : descendre à 1 doit passer.
        // `AdjustOnHand` prend désormais un acteur, un motif et un instant, et rend
        // le MOUVEMENT à consigner (lot 7.3, ISSUE-044) : un ajustement ne laissait
        // aucune trace de qui, quand, ni pourquoi.
        article.AdjustOnHand(-11, actorUserId: null, reason: "inventaire", UnArticleDeStock.Maintenant)
            .IsSuccess.Should().BeTrue();
        article.OnHand.Should().Be(1);
        article.Available.Should().Be(1);
    }
}
