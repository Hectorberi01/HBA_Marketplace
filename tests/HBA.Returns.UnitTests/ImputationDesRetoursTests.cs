using FluentAssertions;
using HBA.Orders.Domain.Orders;
using Xunit;

using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Returns.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE VOLET ORDER-SERVICE D'ISSUE-014.
///
/// `OrderingModuleApi.GetOrderReturnContextAsync` répondait
/// `AlreadyReturnedQuantity: 0` et `AlreadyRefundedAmount: 0m` EN DUR. Ces tests
/// éprouvent la source qui manquait : ce que l'agrégat commande retient d'un
/// dossier de retour, et ce qu'il en rend ensuite.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ImputationDesRetoursTests
{
    private static readonly DateTime Maintenant = new(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Un_retour_rembourse_impute_la_quantite_et_le_montant()
    {
        var commande = Commande(quantite: 3, prixUnitaire: 5_000m);
        var ligne = commande.Lines.First().Id;
        var dossier = Guid.NewGuid();

        commande.RecordReturnSettlement(dossier, 10_000m, [new ReturnSettlementLineDraft(ligne, 2)], Maintenant)
            .IsSuccess.Should().BeTrue();

        commande.RefundedAmount.Should().Be(10_000m);
        commande.ReturnedQuantityFor(ligne).Should().Be(2);
    }

    /// <summary>
    /// LA GARDE QUI TIENT SANS L'INBOX.
    ///
    /// Kafka livre au moins une fois. Si l'agrégat ADDITIONNAIT au lieu de POSER,
    /// un simple rejeu doublerait la marchandise rendue — et fermerait
    /// définitivement le plafond de remboursement d'un client qui n'a rien
    /// demandé de plus.
    /// </summary>
    [Fact]
    public void Un_message_rejoue_n_impute_rien_de_plus()
    {
        var commande = Commande(quantite: 3, prixUnitaire: 5_000m);
        var ligne = commande.Lines.First().Id;
        var dossier = Guid.NewGuid();
        ReturnSettlementLineDraft[] lignes = [new(ligne, 2)];

        commande.RecordReturnSettlement(dossier, 10_000m, lignes, Maintenant);
        commande.RecordReturnSettlement(dossier, 10_000m, lignes, Maintenant.AddSeconds(30));
        commande.RecordReturnSettlement(dossier, 10_000m, lignes, Maintenant.AddMinutes(5));

        commande.RefundedAmount.Should().Be(10_000m);
        commande.ReturnedQuantityFor(ligne).Should().Be(2);
    }

    /// <summary>
    /// Deux partitions Kafka ne garantissent aucun ordre entre elles. Un message
    /// ancien remis après un récent ne doit pas faire REBAISSER le montant
    /// remboursé — ce qui reviendrait à rouvrir un plafond déjà consommé.
    /// </summary>
    [Fact]
    public void Un_message_ancien_ne_fait_pas_reculer_le_compteur()
    {
        var commande = Commande(quantite: 3, prixUnitaire: 5_000m);
        var ligne = commande.Lines.First().Id;
        var dossier = Guid.NewGuid();

        commande.RecordReturnSettlement(dossier, 15_000m, [new ReturnSettlementLineDraft(ligne, 3)], Maintenant);
        commande.RecordReturnSettlement(dossier, 5_000m, [new ReturnSettlementLineDraft(ligne, 1)], Maintenant.AddSeconds(1));

        commande.RefundedAmount.Should().Be(15_000m);
        commande.ReturnedQuantityFor(ligne).Should().Be(3);
    }

    [Fact]
    public void Deux_dossiers_distincts_s_additionnent()
    {
        var commande = Commande(quantite: 3, prixUnitaire: 5_000m);
        var ligne = commande.Lines.First().Id;

        commande.RecordReturnSettlement(Guid.NewGuid(), 5_000m, [new ReturnSettlementLineDraft(ligne, 1)], Maintenant);
        commande.RecordReturnSettlement(Guid.NewGuid(), 5_000m, [new ReturnSettlementLineDraft(ligne, 1)], Maintenant);

        commande.RefundedAmount.Should().Be(10_000m);
        commande.ReturnedQuantityFor(ligne).Should().Be(2);
    }

    /// <summary>
    /// Une ligne qu'on ne sait pas rapprocher n'imputera jamais rien. Refuser le
    /// message entier ferait perdre le MONTANT, donc laisserait le plafond de la
    /// commande grand ouvert — l'inverse de ce qu'on cherche.
    /// </summary>
    [Fact]
    public void Une_ligne_inconnue_est_ignoree_mais_le_montant_reste_impute()
    {
        var commande = Commande(quantite: 3, prixUnitaire: 5_000m);
        var inconnue = Guid.NewGuid();

        commande.RecordReturnSettlement(Guid.NewGuid(), 5_000m, [new ReturnSettlementLineDraft(inconnue, 1)], Maintenant);

        commande.RefundedAmount.Should().Be(5_000m);
        commande.ReturnedQuantityFor(inconnue).Should().Be(0);
    }

    [Fact]
    public void La_quantite_rendue_ne_depasse_jamais_la_quantite_commandee()
    {
        var commande = Commande(quantite: 2, prixUnitaire: 5_000m);
        var ligne = commande.Lines.First().Id;

        commande.RecordReturnSettlement(Guid.NewGuid(), 10_000m, [new ReturnSettlementLineDraft(ligne, 2)], Maintenant);
        commande.RecordReturnSettlement(Guid.NewGuid(), 5_000m, [new ReturnSettlementLineDraft(ligne, 1)], Maintenant);

        commande.ReturnedQuantityFor(ligne).Should().Be(2);
    }

    [Fact]
    public void Un_montant_negatif_est_refuse()
    {
        var commande = Commande(quantite: 1, prixUnitaire: 5_000m);

        var resultat = commande.RecordReturnSettlement(Guid.NewGuid(), -1m, [], Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("order.return_settlement.amount_invalid");
    }

    [Fact]
    public void Un_dossier_sans_identite_est_refuse()
    {
        var commande = Commande(quantite: 1, prixUnitaire: 5_000m);

        var resultat = commande.RecordReturnSettlement(Guid.Empty, 100m, [], Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("order.return_settlement.identity_required");
    }

    /// <summary>
    /// L'état d'origine : rien n'est revenu, rien n'a été rendu. C'est ce que
    /// l'API publique répondait EN DUR quel que soit l'historique de la commande.
    /// </summary>
    [Fact]
    public void Une_commande_sans_retour_ne_declare_rien()
    {
        var commande = Commande(quantite: 3, prixUnitaire: 5_000m);

        commande.RefundedAmount.Should().Be(0m);
        commande.ReturnedQuantityFor(commande.Lines.First().Id).Should().Be(0);
    }

    private static OrderAggregate Commande(int quantite, decimal prixUnitaire)
    {
        var creation = OrderAggregate.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "XOF",
            [
                new OrderLineDraft(
                    OfferId: Guid.NewGuid(),
                    ProductId: Guid.NewGuid(),
                    SellerId: Guid.NewGuid(),
                    Sku: "SKU-1",
                    ShipFromLocationId: Guid.NewGuid(),
                    Quantity: quantite,
                    UnitBasePrice: prixUnitaire,
                    SellerDiscount: 0m,
                    PlatformDiscount: 0m,
                    FinalUnitPrice: prixUnitaire)
            ]);

        creation.IsSuccess.Should().BeTrue(because: creation.IsFailure ? creation.Error.Message : string.Empty);
        return creation.Value;
    }
}
