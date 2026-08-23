using HBA.Inventory.Domain.Stock;
using HBA.Inventory.Domain.Stock.Events;

namespace HBA.Inventory.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-031 — « les réservations expirées ne sont jamais libérées » (CRITICAL).
///
/// `ExpiresAtUtc` était écrite à chaque réservation et relue par PERSONNE : aucun
/// `BackgroundService` n'existait dans inventory. Toute réservation non confirmée
/// immobilisait son stock définitivement, et le stock vendable s'érodait à chaque
/// panier abandonné — silencieusement, cumulativement.
///
/// Ces tests portent sur la règle d'agrégat (`ExpireReservations`), que le
/// balayeur ne fait qu'appeler en boucle. Le CÂBLAGE du balayeur lui-même n'est
/// pas couvert ici : voir l'encadré du `.csproj`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ExpirationDesReservationsTests
{
    [Fact]
    public void Une_reservation_depassee_est_expiree_et_son_stock_revient_a_la_vente()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var abandonnee = Guid.NewGuid();
        article.Reserve(abandonnee, 4, UnArticleDeStock.IlYAUneHeure);

        article.Reserved.Should().Be(4, "avant balayage, le stock est bien immobilisé");

        var bilan = article.ExpireReservations(UnArticleDeStock.Maintenant);

        bilan.Count.Should().Be(1);
        bilan.Quantity.Should().Be(4, "c'est le VOLUME que l'audit demande de journaliser");
        article.Reserved.Should().Be(0);
        article.Available.Should().Be(10);
        article.OnHand.Should().Be(10, "une expiration ne touche pas le stock physique");

        var reservation = article.Reservation(abandonnee);
        reservation.Status.Should().Be(ReservationStatus.Expired);
        reservation.ExpiredAtUtc.Should().Be(UnArticleDeStock.Maintenant);
    }

    /// <summary>
    /// LE TEST QUI PROTÈGE LA VENTE. Une réservation confirmée a TOUJOURS une
    /// échéance dépassée — la vente date d'il y a des semaines. La reprendre
    /// rendrait à la vente un stock déjà retiré d'`OnHand` et déjà facturé.
    /// </summary>
    [Fact]
    public void Une_reservation_confirmee_expiree_n_est_jamais_touchee()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var vendue = Guid.NewGuid();
        article.Reserve(vendue, 4, UnArticleDeStock.IlYAUneHeure);
        article.ConfirmReservation(vendue, UnArticleDeStock.Maintenant.AddHours(-2));

        var bilan = article.ExpireReservations(UnArticleDeStock.Maintenant.AddDays(30));

        bilan.IsEmpty.Should().BeTrue();
        bilan.Quantity.Should().Be(0);
        article.OnHand.Should().Be(6);
        article.Available.Should().Be(6, "le stock vendu ne doit surtout pas réapparaître");

        var reservation = article.Reservation(vendue);
        reservation.Status.Should().Be(ReservationStatus.Confirmed);
        reservation.ExpiredAtUtc.Should().BeNull();
    }

    [Fact]
    public void Une_reservation_encore_dans_les_temps_n_est_pas_touchee()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var enCours = Guid.NewGuid();
        article.Reserve(enCours, 4, UnArticleDeStock.DansUnQuartDHeure);

        var bilan = article.ExpireReservations(UnArticleDeStock.Maintenant);

        bilan.IsEmpty.Should().BeTrue();
        article.Reserved.Should().Be(4);
        article.Available.Should().Be(6);
        article.Reservation(enCours).Status.Should().Be(ReservationStatus.Active);
    }

    /// <summary>
    /// Le balayage est rejoué à chaque tour du travailleur : un second passage
    /// doit être un no-op complet, sinon le journal annoncerait indéfiniment un
    /// volume libéré qui n'existe plus.
    /// </summary>
    [Fact]
    public void Un_second_balayage_ne_libere_rien_de_plus()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        article.Reserve(Guid.NewGuid(), 4, UnArticleDeStock.IlYAUneHeure);

        article.ExpireReservations(UnArticleDeStock.Maintenant).Quantity.Should().Be(4);
        var versionApresPremierBalayage = article.StockVersion;

        var second = article.ExpireReservations(UnArticleDeStock.Maintenant.AddMinutes(5));

        second.IsEmpty.Should().BeTrue();
        second.Quantity.Should().Be(0);
        article.Available.Should().Be(10);
        article.StockVersion.Should().Be(
            versionApresPremierBalayage, "un tour à vide n'écrit rien — voir Touch()");
    }

    /// <summary>
    /// Un seul balayage traite toutes les réservations dépassées de l'article, et
    /// laisse les autres en place. Le volume rendu est la somme des seules
    /// expirées.
    /// </summary>
    [Fact]
    public void Le_balayage_additionne_le_volume_de_toutes_les_reservations_depassees()
    {
        var article = UnArticleDeStock.Avec(onHand: 20);
        article.Reserve(Guid.NewGuid(), 3, UnArticleDeStock.IlYAUneHeure);
        article.Reserve(Guid.NewGuid(), 5, UnArticleDeStock.IlYAUneHeure);
        article.Reserve(Guid.NewGuid(), 2, UnArticleDeStock.DansUnQuartDHeure);

        var bilan = article.ExpireReservations(UnArticleDeStock.Maintenant);

        bilan.Count.Should().Be(2);
        bilan.Quantity.Should().Be(8);
        article.Reserved.Should().Be(2);
        article.Available.Should().Be(18);
    }

    /// <summary>
    /// SANS CET ÉVÉNEMENT, ISSUE-031 SERAIT CORRIGÉE EN BASE ET INVISIBLE POUR
    /// L'ACHETEUR. L'article était passé « en rupture » (`StockDepleted`), donc
    /// l'offre a été retirée de la vente. Rendre le stock sans le dire laisserait
    /// l'offre éteinte pour toujours.
    /// </summary>
    [Fact]
    public void Un_article_epuise_puis_balaye_annonce_son_reapprovisionnement()
    {
        var article = UnArticleDeStock.Avec(onHand: 5);
        article.Reserve(Guid.NewGuid(), 5, UnArticleDeStock.IlYAUneHeure);
        article.Available.Should().Be(0);

        article.ClearDomainEvents();
        article.ExpireReservations(UnArticleDeStock.Maintenant);

        article.DomainEvents.Should().ContainSingle(e => e is StockReplenishedDomainEvent);
        article.Available.Should().Be(5);
    }

    /// <summary>Même transition par la porte de la libération volontaire.</summary>
    [Fact]
    public void Un_article_epuise_puis_libere_annonce_son_reapprovisionnement()
    {
        var article = UnArticleDeStock.Avec(onHand: 5);
        var commande = Guid.NewGuid();
        article.Reserve(commande, 5, UnArticleDeStock.DansUnQuartDHeure);

        article.ClearDomainEvents();
        article.ReleaseReservation(commande, UnArticleDeStock.Maintenant);

        article.DomainEvents.Should().ContainSingle(e => e is StockReplenishedDomainEvent);
        article.Available.Should().Be(5);
    }
}
