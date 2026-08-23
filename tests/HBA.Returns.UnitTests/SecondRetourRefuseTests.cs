using FluentAssertions;
using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Policies;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using Xunit;

namespace HBA.Returns.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE VOLET RETURN-REFUND D'ISSUE-014 : « SECOND RETOUR SUR LE MÊME ARTICLE →
/// REFUS ».
///
/// C'est le test que l'audit exige nommément. Il n'avait aucune chance de passer
/// avant la correction : order-service affirmait à chaque appel
/// `AlreadyReturnedQuantity: 0`, donc `disponible` valait toujours la quantité
/// livrée entière, et un second dossier était accepté aussi facilement que le
/// premier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SecondRetourRefuseTests
{
    [Fact]
    public void Le_premier_retour_est_accepte()
    {
        var creation = ReturnItem.Create(Ligne(livree: 2, dejaRevenue: 0, demandee: 2));

        creation.IsSuccess.Should().BeTrue();
        creation.Value.RequestedQuantity.Should().Be(2);
    }

    /// <summary>
    /// Tout est déjà revenu : il ne reste rien à reprendre. C'est exactement le
    /// scénario « un même article retourné et remboursé autant de fois que
    /// voulu » de l'audit.
    /// </summary>
    [Fact]
    public void Un_second_retour_sur_un_article_deja_revenu_est_refuse()
    {
        var creation = ReturnItem.Create(Ligne(livree: 2, dejaRevenue: 2, demandee: 1));

        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("return.item.quantity_invalid");
    }

    [Fact]
    public void Un_retour_partiel_laisse_le_reste_retournable()
    {
        ReturnItem.Create(Ligne(livree: 3, dejaRevenue: 2, demandee: 1)).IsSuccess.Should().BeTrue();
        ReturnItem.Create(Ligne(livree: 3, dejaRevenue: 2, demandee: 2)).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ET LE PLAFOND NE DOIT PAS COMPTER DEUX FOIS.
    ///
    /// `Validate` vérifie `demandé + engagé > plafond`, où le plafond vaut
    /// `CapturedAmount − AlreadyRefundedAmount`. Tant qu'`AlreadyRefundedAmount`
    /// valait zéro, `engagé` devait porter TOUT ce que le dossier avait engagé,
    /// versements aboutis compris. Maintenant qu'order-service les déduit déjà du
    /// plafond, les repasser ici les compterait deux fois — et refuserait un
    /// remboursement parfaitement légitime.
    ///
    /// C'est la question qu'a soulevée l'implémentation du côté return-refund, et
    /// ces deux cas sont sa réponse.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Le_plafond_ne_compte_pas_deux_fois_un_versement_deja_deduit()
    {
        // Commande encaissée 10 000, dont 4 000 déjà remboursés par CE dossier.
        // Order-service l'a inscrit : le plafond restant vaut 6 000.
        var plafondRestant = 6_000m;
        var detail = Detail(items: 6_000m, remboursementsAnterieurs: 0m);

        // Ce qui reste engagé et invisible d'order-service : rien.
        var resultat = RefundCalculationPolicy.Validate(
            new Money(6_000m, "XOF"), detail, plafondRestant, alreadyRefunded: 0m);

        resultat.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Le_plafond_compte_ce_qui_est_decide_mais_pas_encore_verse()
    {
        // Plafond restant 6 000, mais 5 000 sont déjà décidés et attendent leur
        // versement : order-service ne les voit pas encore.
        var resultat = RefundCalculationPolicy.Validate(
            new Money(6_000m, "XOF"),
            Detail(items: 6_000m, remboursementsAnterieurs: 0m),
            capturedRemainingCeiling: 6_000m,
            alreadyRefunded: 5_000m);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("refund.amount_exceeds_available");
    }

    private static ReturnItemDraft Ligne(int livree, int dejaRevenue, int demandee)
        => new(
            OrderItemId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            VariantId: null,
            SkuSnapshot: "SKU-1",
            NameSnapshot: "Article",
            OrderedQuantity: livree,
            DeliveredQuantity: livree,
            AlreadyReturnedQuantity: dejaRevenue,
            RequestedQuantity: demandee,
            UnitPaidAmount: new Money(5_000m, "XOF"),
            ReasonCode: ReturnReasonCode.Defective,
            ConditionDeclared: InspectionCondition.Damaged);

    private static RefundBreakdown Detail(decimal items, decimal remboursementsAnterieurs)
    {
        var zero = Money.Zero("XOF");
        return new RefundBreakdown(
            Items: new Money(items, "XOF"),
            Tax: zero,
            OriginalShipping: zero,
            DiscountAllocation: zero,
            RestockingFee: zero,
            ReturnShippingCharge: zero,
            PreviousRefunds: new Money(remboursementsAnterieurs, "XOF"));
    }
}
