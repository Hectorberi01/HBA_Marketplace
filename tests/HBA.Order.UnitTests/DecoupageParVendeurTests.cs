using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.Events;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Order.UnitTests;

/// <summary>ISSUE-027 — « l'agrégat `SellerOrder` n'existe pas » (CRITICAL).</summary>
public sealed class DecoupageParVendeurTests
{
    [Fact]
    public void Une_commande_a_deux_vendeurs_produit_deux_parts_independantes()
    {
        var vendeurA = Guid.NewGuid();
        var vendeurB = Guid.NewGuid();

        var commande = UneCommande.Confirmee(
            UneCommande.Marchandise(vendeurA, quantite: 2, prixUnitaire: 1000m, sku: "SKU-A"),
            UneCommande.Marchandise(vendeurB, quantite: 1, prixUnitaire: 4000m, sku: "SKU-B"));

        var parts = SellerOrder.SplitFrom(commande, UneCommande.Maintenant);

        parts.IsSuccess.Should().BeTrue();
        parts.Value.Should().HaveCount(2);
        parts.Value.Select(p => p.SellerId).Should().BeEquivalentTo(new[] { vendeurA, vendeurB });

        // Deux identités distinctes : ce sont deux agrégats, pas deux vues du même.
        parts.Value.Select(p => p.Id).Should().OnlyHaveUniqueItems();

        parts.Value.Should().OnlyContain(p => p.Status == SellerOrderStatus.AwaitingConfirmation);
        parts.Value.Should().OnlyContain(p => p.OrderId == commande.Id.Value);
    }

    /// <summary>LE TEST QUE L'AUDIT EXIGE NOMMÉMENT.</summary>
    [Fact]
    public void Un_vendeur_qui_confirme_n_affecte_ni_l_autre_part_ni_la_commande()
    {
        var vendeurA = Guid.NewGuid();
        var vendeurB = Guid.NewGuid();

        var commande = UneCommande.Confirmee(
            UneCommande.Marchandise(vendeurA),
            UneCommande.Marchandise(vendeurB));

        var parts = SellerOrder.SplitFrom(commande, UneCommande.Maintenant).Value;
        var partA = parts.Single(p => p.SellerId == vendeurA);
        var partB = parts.Single(p => p.SellerId == vendeurB);

        partA.Confirm(UneCommande.Maintenant).IsSuccess.Should().BeTrue();

        partA.Status.Should().Be(SellerOrderStatus.Confirmed);
        partB.Status.Should().Be(SellerOrderStatus.AwaitingConfirmation,
            "la part d'un vendeur ne dit rien de celle d'un autre");

        // ET LA COMMANDE N'A PAS BOUGÉ.
        commande.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void Chaque_part_ne_porte_que_les_lignes_et_le_montant_de_son_vendeur()
    {
        var vendeurA = Guid.NewGuid();
        var vendeurB = Guid.NewGuid();

        var commande = UneCommande.Confirmee(
            UneCommande.Marchandise(vendeurA, quantite: 2, prixUnitaire: 1000m, sku: "SKU-A1"),
            UneCommande.Marchandise(vendeurA, quantite: 3, prixUnitaire: 500m, sku: "SKU-A2"),
            UneCommande.Marchandise(vendeurB, quantite: 1, prixUnitaire: 4000m, sku: "SKU-B"));

        var parts = SellerOrder.SplitFrom(commande, UneCommande.Maintenant).Value;

        var partA = parts.Single(p => p.SellerId == vendeurA);
        partA.Lines.Should().HaveCount(2);
        partA.ItemCount.Should().Be(5);
        partA.Amount.Should().Be(3500m);
        partA.Lines.Select(l => l.Sku).Should().BeEquivalentTo(new[] { "SKU-A1", "SKU-A2" });

        var partB = parts.Single(p => p.SellerId == vendeurB);
        partB.Lines.Should().ContainSingle();
        partB.Amount.Should().Be(4000m);

        // AUCUNE PART NE VOIT LA LIGNE D'UN AUTRE : c'est la même fuite que
        // `OrderMapper.ToSellerSummary` a déjà eu à refermer — du renseignement
        // commercial servi par l'API.
        partB.Lines.Should().NotContain(l => l.Sku.StartsWith("SKU-A"));
    }

    /// <summary>UNE COMMANDE DE REPAS NE PRODUIT AUCUNE PART, ET C'EST UN INVARIANT.</summary>
    [Fact]
    public void Une_commande_de_repas_ne_produit_aucune_part()
    {
        var restaurant = Guid.NewGuid();

        var commande = UneCommande.Confirmee(
            UneCommande.Repas(restaurant),
            UneCommande.Repas(restaurant, quantite: 2));

        var parts = SellerOrder.SplitFrom(commande, UneCommande.Maintenant);

        parts.IsSuccess.Should().BeTrue("une liste vide est le cas NORMAL, pas un échec");
        parts.Value.Should().BeEmpty();
    }

    /// <summary>
    /// Le découpage et la répartition envoyée aux vendeurs doivent désigner
    /// EXACTEMENT les mêmes vendeurs et les mêmes montants — c'est pour cela que le
    /// filtre a été extrait dans `Order.SellerLineGroups()` au lieu d'être recopié.
    /// </summary>
    [Fact]
    public void Le_decoupage_designe_les_memes_vendeurs_que_la_repartition_de_la_confirmation()
    {
        var vendeurA = Guid.NewGuid();
        var vendeurB = Guid.NewGuid();

        var commande = UneCommande.Confirmee(
            UneCommande.Marchandise(vendeurA, quantite: 2, prixUnitaire: 1000m),
            UneCommande.Marchandise(vendeurB, quantite: 1, prixUnitaire: 4000m));

        var repartition = commande.DomainEvents
            .OfType<OrderConfirmedDomainEvent>()
            .Single()
            .SellerShares;

        var parts = SellerOrder.SplitFrom(commande, UneCommande.Maintenant).Value;

        parts.Select(p => (p.SellerId, p.ItemCount, p.Amount))
            .Should()
            .BeEquivalentTo(repartition.Select(s => (s.SellerId, s.ItemCount, s.Amount)));
    }

    /// <summary>AVANT LE PAIEMENT, IL N'Y A RIEN QU'UN VENDEUR PUISSE FAIRE.</summary>
    [Fact]
    public void Une_commande_non_confirmee_ne_se_decoupe_pas()
    {
        var commande = UneCommande.Creee(UneCommande.Marchandise(Guid.NewGuid()));

        var parts = SellerOrder.SplitFrom(commande, UneCommande.Maintenant);

        parts.IsFailure.Should().BeTrue();
        parts.Error.Code.Should().Be("ordering.seller_order.order_not_confirmed");
    }

    /// <summary>LA CONFIRMATION REJOUÉE NE PRODUIT PAS UN SECOND DÉCOUPAGE.</summary>
    [Fact]
    public void Une_commande_deja_confirmee_refuse_un_second_passage_donc_un_second_decoupage()
    {
        var commande = UneCommande.Confirmee(UneCommande.Marchandise(Guid.NewGuid()));

        // Le rejeu recommence par là où le gestionnaire commence.
        var secondPaiement = commande.MarkPaid(Guid.NewGuid());
        secondPaiement.IsFailure.Should().BeTrue();
        secondPaiement.Error.Code.Should().Be("ordering.invalid_transition");

        var secondeConfirmation = commande.Confirm();
        secondeConfirmation.IsFailure.Should().BeTrue();
        secondeConfirmation.Error.Code.Should().Be("ordering.invalid_transition");
    }
}
