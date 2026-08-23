using HBA.Orders.Domain.Orders.SellerOrders;
using HBA.Orders.Domain.Orders.SellerOrders.Events;

namespace HBA.Order.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN VENDEUR REFUSE SA PART D'UNE COMMANDE DÉJÀ PAYÉE.
///
/// CE QUE CES TESTS ÉPROUVENT, ET CE QU'ILS N'ÉPROUVENT PAS.
///
/// Ils vérifient que le refus est TRAÇABLE — motif obligatoire — et que
/// l'événement porte de quoi agir : les lignes, leur emplacement d'expédition, le
/// montant, la devise. Ils ne vérifient PAS qu'il se passe quoi que ce soit
/// ensuite, parce qu'il ne se passe rien :
/// `SellerOrderRefusedIntegrationEvent` n'a aucun consommateur, donc un refus
/// vendeur ne libère pas le stock, ne rembourse pas la part et ne prévient pas le
/// client. Les trois gestes vivent dans inventory-service, financial-service et
/// communication-service.
///
/// Écrire un test qui affirmerait le contraire serait pire que ne pas en écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RefusDuVendeurTests
{
    private static SellerOrder UnePartVendeur(Guid vendeur, int quantite = 2, decimal prix = 1500m)
    {
        var commande = UneCommande.Confirmee(
            UneCommande.Marchandise(vendeur, quantite, prix, sku: "SKU-REFUS"));

        return SellerOrder.SplitFrom(commande, UneCommande.Maintenant).Value.Single();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_refus_sans_motif_est_refuse(string motif)
    {
        var part = UnePartVendeur(Guid.NewGuid());

        var resultat = part.Reject(motif, UneCommande.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.seller_order.reason_required");

        // RIEN N'A BOUGÉ : ni l'état, ni l'événement. Un refus mal formé ne doit
        // surtout pas laisser derrière lui un message annonçant un refus.
        part.Status.Should().Be(SellerOrderStatus.AwaitingConfirmation);
        part.RefusalReason.Should().BeNull();
        part.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Une_annulation_sans_motif_est_refusee_elle_aussi()
    {
        var part = UnePartVendeur(Guid.NewGuid());
        part.Confirm(UneCommande.Maintenant);

        var resultat = part.Cancel("  ", UneCommande.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.seller_order.reason_required");
        part.Status.Should().Be(SellerOrderStatus.Confirmed);
    }

    /// <summary>
    /// L'ÉVÉNEMENT DOIT PORTER DE QUOI AGIR, SANS RELIRE LA COMMANDE.
    ///
    /// `ShipFromLocationId` est le champ qu'on oublie : Inventory travaille par
    /// (SKU, emplacement, commande), et sans lui aucun consommateur ne peut rendre
    /// le stock — il ne le découvrirait qu'en écrivant son gestionnaire.
    /// </summary>
    [Fact]
    public void Un_refus_motive_ferme_la_part_et_leve_un_evenement_exploitable()
    {
        var vendeur = Guid.NewGuid();
        var part = UnePartVendeur(vendeur, quantite: 2, prix: 1500m);

        var resultat = part.Reject("Rupture de stock en boutique.", UneCommande.Maintenant);

        resultat.IsSuccess.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.Rejected);
        part.IsOpen.Should().BeFalse();
        part.RefusedAtUtc.Should().Be(UneCommande.Maintenant);
        part.RefusalReason.Should().Be("Rupture de stock en boutique.");

        var evenement = part.DomainEvents.OfType<SellerOrderRefusedDomainEvent>().Single();
        evenement.Outcome.Should().Be("Rejected");
        evenement.SellerId.Should().Be(vendeur);
        evenement.OrderId.Should().Be(part.OrderId);
        evenement.BuyerId.Should().Be(part.BuyerId);
        evenement.Currency.Should().Be("XOF");
        evenement.Amount.Should().Be(3000m);
        evenement.Reason.Should().Be("Rupture de stock en boutique.");

        var ligne = evenement.Lines.Should().ContainSingle().Subject;
        ligne.Sku.Should().Be("SKU-REFUS");
        ligne.Quantity.Should().Be(2);
        ligne.LineTotal.Should().Be(3000m);
        ligne.ShipFromLocationId.Should().NotBeEmpty(
            "sans l'emplacement, aucun consommateur ne peut rendre le stock");
        ligne.OrderLineId.Should().NotBeEmpty(
            "c'est par la LIGNE qu'un retour se rapproche, pas par le produit");
    }

    /// <summary>
    /// REFUSER ET SE DÉDIRE SONT DEUX GESTES, ET DEUX PERMISSIONS.
    ///
    /// `ORDER_REJECT` est normale, `ORDER_CANCEL` est SENSIBLE : se dédire après
    /// avoir fait attendre le client n'est pas la même chose que refuser tout de
    /// suite. Le message d'erreur désigne l'autre geste, parce que l'autre geste
    /// existe — sans quoi le vendeur irait au support pour un clic.
    /// </summary>
    [Fact]
    public void Une_part_deja_confirmee_ne_se_refuse_plus_elle_s_annule()
    {
        var part = UnePartVendeur(Guid.NewGuid());
        part.Confirm(UneCommande.Maintenant);

        var refus = part.Reject("Trop tard.", UneCommande.Maintenant);

        refus.IsFailure.Should().BeTrue();
        refus.Error.Code.Should().Be("ordering.seller_order.already_engaged");

        part.Cancel("Article cassé à l'emballage.", UneCommande.Maintenant).IsSuccess.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.Cancelled);
        part.DomainEvents.OfType<SellerOrderRefusedDomainEvent>().Single().Outcome.Should().Be("Cancelled");
    }

    [Fact]
    public void Une_part_non_confirmee_ne_s_annule_pas_elle_se_refuse()
    {
        var part = UnePartVendeur(Guid.NewGuid());

        var resultat = part.Cancel("Peu importe.", UneCommande.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.seller_order.not_yet_engaged");
        part.Status.Should().Be(SellerOrderStatus.AwaitingConfirmation);
    }

    /// <summary>
    /// LE COLIS EST PARTI : LE REPRENDRE N'EST PLUS UNE ANNULATION, C'EST UN
    /// RETOUR. Même invariant que `Order.Cancel` à l'échelle de la commande — une
    /// vente conclue ne s'annule pas, elle se retourne.
    /// </summary>
    [Fact]
    public void Une_part_remise_au_livreur_ne_s_annule_plus()
    {
        var part = UnePartVendeur(Guid.NewGuid());
        part.Confirm(UneCommande.Maintenant);
        part.MarkPreparing(UneCommande.Maintenant);
        part.MarkReadyForPickup(UneCommande.Maintenant);
        part.MarkHandedOver(UneCommande.Maintenant);

        var resultat = part.Cancel("Trop tard.", UneCommande.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.seller_order.already_closed");
        part.Status.Should().Be(SellerOrderStatus.HandedOver);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMMANDE ENTIÈRE TOMBE : LA PART TOMBE, MAIS SANS ÉVÉNEMENT DE REFUS.
    ///
    /// C'EST LE POINT LE PLUS FACILE À CASSER PAR MÉGARDE.
    ///
    /// `OrderCancelled` est déjà parti et c'est LUI que financial-service consomme
    /// pour rembourser — la totalité, puisque la commande entière tombe. Lever ici
    /// un refus vendeur en plus ferait, le jour où ce refus aura un consommateur,
    /// rembourser une SECONDE fois chaque part d'une commande déjà intégralement
    /// remboursée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Une_part_fermee_avec_la_commande_ne_leve_aucun_evenement_de_refus()
    {
        var part = UnePartVendeur(Guid.NewGuid());
        part.Confirm(UneCommande.Maintenant);
        part.MarkPreparing(UneCommande.Maintenant);

        var resultat = part.CancelWithOrder("Arbitrage : commande non livrable.", UneCommande.Maintenant);

        resultat.IsSuccess.Should().BeTrue();
        part.Status.Should().Be(SellerOrderStatus.Cancelled);
        part.RefusalReason.Should().Be("Arbitrage : commande non livrable.");
        part.DomainEvents.OfType<SellerOrderRefusedDomainEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// UN REFUS ANTÉRIEUR GARDE SON MOTIF. C'est l'HISTOIRE : l'écraser par
    /// « commande annulée » ferait perdre la CAUSE au profit de la conséquence —
    /// la même erreur que `ReviewReason` évite côté commande.
    /// </summary>
    [Fact]
    public void Une_part_deja_refusee_n_est_pas_reecrite_par_l_annulation_de_la_commande()
    {
        var part = UnePartVendeur(Guid.NewGuid());
        part.Reject("Article introuvable en rayon.", UneCommande.Maintenant);

        var resultat = part.CancelWithOrder("Commande annulée.", UneCommande.Maintenant.AddHours(2));

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.seller_order.already_closed");
        part.Status.Should().Be(SellerOrderStatus.Rejected);
        part.RefusalReason.Should().Be("Article introuvable en rayon.");
    }
}
