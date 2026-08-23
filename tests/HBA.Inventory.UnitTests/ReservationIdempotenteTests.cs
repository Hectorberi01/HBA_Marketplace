using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-075 — « `ReserveStock` n'est pas idempotent » (CRITICAL).
///
/// L'appelant, `PlaceOrderCommandHandler`, vit derrière une échéance de 5 s. Un
/// dépassement suivi d'un rejeu réservait DEUX fois : le stock disparaissait deux
/// fois pour une seule vente, et la moitié n'était portée par aucune commande —
/// donc libérée par personne.
///
/// Ces tests n'avaient aucune chance de passer avant la correction : `Reserve`
/// faisait un `_reservations.Add(...)` inconditionnel.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ReservationIdempotenteTests
{
    [Fact]
    public void Un_rejeu_a_l_identique_ne_cree_pas_de_seconde_reservation()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();

        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure).IsSuccess.Should().BeTrue();
        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure).IsSuccess.Should().BeTrue();

        article.Reservations.Should().HaveCount(1);
        article.Reserved.Should().Be(3);
        article.Available.Should().Be(7);
    }

    /// <summary>
    /// Le rejeu strictement identique ne doit RIEN écrire du tout : ni la ligne
    /// enfant, ni le compteur du verrou optimiste. Sinon deux rejeux inoffensifs
    /// se battraient sur `xmin` et l'un des deux repartirait en 409.
    /// </summary>
    [Fact]
    public void Un_rejeu_a_l_identique_ne_salit_pas_la_ligne_parente()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();

        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure);
        var versionApresPremiere = article.StockVersion;

        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure);

        article.StockVersion.Should().Be(versionApresPremiere);
    }

    /// <summary>
    /// Deux COMMANDES distinctes réservent bien chacune la leur : l'idempotence
    /// porte sur le couple (article, commande), pas sur l'article.
    /// </summary>
    [Fact]
    public void Deux_commandes_distinctes_reservent_chacune_la_sienne()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);

        article.Reserve(Guid.NewGuid(), 3, UnArticleDeStock.DansUnQuartDHeure);
        article.Reserve(Guid.NewGuid(), 4, UnArticleDeStock.DansUnQuartDHeure);

        article.Reservations.Should().HaveCount(2);
        article.Reserved.Should().Be(7);
        article.Available.Should().Be(3);
    }

    /// <summary>
    /// La quantité est POSÉE, pas ajoutée — et le disponible ne compte pas deux
    /// fois ce que cette commande détient déjà. Sur 10 en stock avec 6 déjà
    /// réservés par cette commande, passer à 9 doit passer : il reste 4 libres, et
    /// l'extension ne demande que 3 de plus.
    /// </summary>
    [Fact]
    public void Une_quantite_revue_a_la_hausse_ne_compte_pas_deux_fois_la_commande()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();

        article.Reserve(commande, 6, UnArticleDeStock.DansUnQuartDHeure).IsSuccess.Should().BeTrue();

        var extension = article.Reserve(commande, 9, UnArticleDeStock.DansUnQuartDHeure);

        extension.IsSuccess.Should().BeTrue();
        article.Reservations.Should().HaveCount(1);
        article.Reserved.Should().Be(9);
        article.Available.Should().Be(1);
    }

    [Fact]
    public void Une_quantite_revue_a_la_baisse_rend_la_difference_a_la_vente()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();

        article.Reserve(commande, 6, UnArticleDeStock.DansUnQuartDHeure);
        article.Reserve(commande, 2, UnArticleDeStock.DansUnQuartDHeure);

        article.Reserved.Should().Be(2);
        article.Available.Should().Be(8);
    }

    /// <summary>
    /// L'extension reste soumise au stock réel : elle échoue si le total demandé
    /// dépasse ce que l'article peut tenir, sans casser la réservation en place.
    /// </summary>
    [Fact]
    public void Une_extension_au_dela_du_stock_est_refusee_et_ne_casse_rien()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();

        article.Reserve(commande, 6, UnArticleDeStock.DansUnQuartDHeure);

        var trop = article.Reserve(commande, 11, UnArticleDeStock.DansUnQuartDHeure);

        trop.IsFailure.Should().BeTrue();
        trop.Error.Code.Should().Be("inventory.item.insufficient_stock");
        article.Reserved.Should().Be(6);
        article.Available.Should().Be(4);
    }

    /// <summary>
    /// Un rejeu ne doit jamais RACCOURCIR la fenêtre : le balayeur libérerait
    /// alors le stock sous les pieds d'un paiement en cours d'aboutissement.
    /// </summary>
    [Fact]
    public void Un_rejeu_ne_raccourcit_jamais_la_fenetre_d_expiration()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();
        var lointaine = UnArticleDeStock.Maintenant.AddMinutes(30);

        article.Reserve(commande, 3, lointaine);
        article.Reserve(commande, 3, UnArticleDeStock.Maintenant.AddMinutes(1));

        article.Reservation(commande).ExpiresAtUtc.Should().Be(lointaine);
    }

    /// <summary>
    /// Une commande dont la réservation a été LIBÉRÉE peut en reprendre une
    /// nouvelle : c'est la reprise de paiement la plus banale. Deux lignes
    /// subsistent alors pour le même couple — une `Released`, une `Active` — et
    /// c'est exactement pourquoi l'index unique est PARTIEL.
    /// </summary>
    [Fact]
    public void Une_commande_liberee_peut_reserver_a_nouveau()
    {
        var article = UnArticleDeStock.Avec(onHand: 10);
        var commande = Guid.NewGuid();

        article.Reserve(commande, 3, UnArticleDeStock.DansUnQuartDHeure);
        article.ReleaseReservation(commande, UnArticleDeStock.Maintenant);
        article.Reserve(commande, 4, UnArticleDeStock.DansUnQuartDHeure).IsSuccess.Should().BeTrue();

        article.Reservations.Should().HaveCount(2);
        article.Reservations.Count(r => r.Status == ReservationStatus.Active).Should().Be(1);
        article.Reservations.Count(r => r.Status == ReservationStatus.Released).Should().Be(1);
        article.Reserved.Should().Be(4);
        article.Available.Should().Be(6);
    }
}
