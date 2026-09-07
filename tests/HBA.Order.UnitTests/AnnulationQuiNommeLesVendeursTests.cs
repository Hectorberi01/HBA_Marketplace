using HBA.Orders.Domain.Orders.Events;

namespace HBA.Order.UnitTests;

/// <summary>L'ANNULATION DIT MAINTENANT QUI PERD ET COMBIEN — LOT 2.</summary>
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

    /// <summary>LA RÉPARTITION DE L'ANNULATION EST LA MÊME QUE CELLE DE LA CONFIRMATION.</summary>
    [Fact]
    public void La_repartition_de_l_annulation_est_celle_de_la_confirmation()
    {
        var vendeur = Guid.NewGuid();

        var commande = UneCommande.Confirmee(
            UneCommande.Marchandise(vendeur, quantite: 3, prixUnitaire: 1500m, sku: "SKU-C"));

        var aLaConfirmation = commande.DomainEvents
            .OfType<OrderConfirmedDomainEvent>().Single().SellerShares;

        // `CancelAfterReview` est la seule sortie d'une commande CONFIRMÉE —
        // `Cancel` la refuse, et c'est l'invariant « une vente conclue ne s'annule
        // pas, elle se retourne ».
        commande.MarkUnderReview("course annulée").IsSuccess.Should().BeTrue();
        commande.CancelAfterReview("remboursement décidé").IsSuccess.Should().BeTrue();

        var aLAnnulation = commande.DomainEvents
            .OfType<OrderCancelledDomainEvent>().Single().SellerShares;

        aLAnnulation.Should().BeEquivalentTo(aLaConfirmation);
    }

    /// <summary>UNE COMMANDE DE REPAS REND UNE LISTE VIDE, ET SURTOUT PAS `null`.</summary>
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
