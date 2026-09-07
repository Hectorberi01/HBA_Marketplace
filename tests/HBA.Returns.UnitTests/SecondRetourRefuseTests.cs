using FluentAssertions;
using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Policies;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using Xunit;

namespace HBA.Returns.UnitTests;

/// <summary>
/// LE VOLET RETURN-REFUND D'ISSUE-014 : « SECOND RETOUR SUR LE MÊME ARTICLE → REFUS
/// ».
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

    /// <summary>Tout est déjà revenu : il ne reste rien à reprendre.</summary>
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

    /// <summary>ET LE PLAFOND NE DOIT PAS COMPTER DEUX FOIS.</summary>
    [Fact]
    public void Le_plafond_ne_compte_pas_deux_fois_un_versement_deja_deduit()
    {
        // Commande encaissée 10 000, dont 4 000 déjà remboursés par CE dossier.
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
