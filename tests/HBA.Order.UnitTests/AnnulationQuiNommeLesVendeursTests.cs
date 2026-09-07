using HBA.Orders.Domain.Orders.Events;

namespace HBA.Order.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ANNULATION DIT MAINTENANT QUI PERD ET COMBIEN — LOT 2.
///
/// `OrderCancelledDomainEvent` ne portait que `(OrderId, BuyerId, Reason)`. On
/// pouvait compter les annulations de la PLATEFORME et rien d'autre : « combien
/// ce vendeur a-t-il perdu ce mois-ci » n'avait aucune source.
///
/// CE QUE CES TESTS TIENNENT, ET QUI SE CASSERAIT EN SILENCE.
///
/// La répartition de l'annulation doit être calculée par le MÊME filtre que celle
/// de la confirmation — `BuildSellerShares`. Deux filtres séparés marcheraient le
/// premier jour et divergeraient ensuite, avec pour symptôme un vendeur débité
/// d'une annulation pour une commande qu'il n'a jamais vue passer.
///
/// C'est le même raisonnement que `DecoupageParVendeurTests` tient entre le
/// découpage et la confirmation.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class AnnulationQuiNommeLesVendeursTests
{
    [Fact]
    public void Une_commande_annulee_dit_qui_perd_et_combien()
    {
        var vendeurA = Guid.NewGuid();
        var vendeurB = Guid.NewGuid();

        var commande = UneCommande.Creee(
            UneCommande.Marchandise(vendeurA, quantite: 2, prixUnitaire: 1000m, sku: "SKU-A"),
            UneCommande.Marchandise(vendeurB, quantite: 1, prixUnitaire: 4000m, sku: "SKU-B"));

        commande.Cancel("changement d'avis").IsSuccess.Should().BeTrue();

        var evenement = commande.DomainEvents.OfType<OrderCancelledDomainEvent>().Single();

        evenement.Currency.Should().Be("XOF");
        evenement.SellerShares.Should().HaveCount(2);
        evenement.SellerShares.Single(part => part.SellerId == vendeurA).Amount.Should().Be(2000m);
        evenement.SellerShares.Single(part => part.SellerId == vendeurB).Amount.Should().Be(4000m);
    }

    /// <summary>
    /// LA RÉPARTITION DE L'ANNULATION EST LA MÊME QUE CELLE DE LA CONFIRMATION.
    /// </summary>
    /// <remarks>
    /// C'est l'invariant qui compte : les deux passent par `BuildSellerShares`,
    /// donc par le même filtre de lignes. Ce test le vérifie sur une commande qui
    /// parcourt les deux transitions, plutôt que sur deux commandes distinctes —
    /// un écart de fabrique masquerait précisément ce qu'on veut voir.
    /// </remarks>
    [Fact]
    public void La_repartition_de_l_annulation_est_celle_de_la_confirmation()
    {
        var vendeur = Guid.NewGuid();

        var commande = UneCommande.Confirmee(
            UneCommande.Marchandise(vendeur, quantite: 3, prixUnitaire: 1500m, sku: "SKU-C"));

        var aLaConfirmation = commande.DomainEvents
            .OfType<OrderConfirmedDomainEvent>().Single().SellerShares;

        // `CancelAfterReview` est la seule sortie d'une commande CONFIRMÉE —
        // `Cancel` la refuse, et c'est l'invariant « une vente conclue ne
        // s'annule pas, elle se retourne ».
        commande.MarkUnderReview("course annulée").IsSuccess.Should().BeTrue();
        commande.CancelAfterReview("remboursement décidé").IsSuccess.Should().BeTrue();

        var aLAnnulation = commande.DomainEvents
            .OfType<OrderCancelledDomainEvent>().Single().SellerShares;

        aLAnnulation.Should().BeEquivalentTo(aLaConfirmation);
    }

    /// <summary>
    /// UNE COMMANDE DE REPAS REND UNE LISTE VIDE, ET SURTOUT PAS `null`.
    /// </summary>
    /// <remarks>
    /// Le champ du CONTRAT est nullable, et son `null` ne veut dire qu'une chose :
    /// « message émis avant le lot 2 ». Une liste vide, elle, est une
    /// information : cette commande n'avait aucun vendeur. Confondre les deux
    /// ferait compter les messages anciens comme de la restauration.
    /// </remarks>
    [Fact]
    public void Une_commande_de_repas_annulee_ne_nomme_aucun_vendeur()
    {
        var commande = UneCommande.Creee(UneCommande.Repas(Guid.NewGuid()));

        commande.Cancel("le restaurant est fermé").IsSuccess.Should().BeTrue();

        var evenement = commande.DomainEvents.OfType<OrderCancelledDomainEvent>().Single();

        evenement.SellerShares.Should().NotBeNull();
        evenement.SellerShares.Should().BeEmpty();
        evenement.Currency.Should().Be("XOF");
    }
}
