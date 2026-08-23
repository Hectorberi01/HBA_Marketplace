using FluentAssertions;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Shared.Domain.Primitives;
using Xunit;

namespace HBA.Payments.Tests;

public sealed class PaymentRefundDomainTests
{
    [Fact]
    public void Un_rejeu_avec_la_meme_cle_renvoie_la_meme_demande()
    {
        var payment = CapturedPayment(100m);
        var amount = Money.Create(25m, "XOF").Value;

        var first = payment.BeginRefund(amount, "retour", "return:1:refund:1", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        var second = payment.BeginRefund(amount, "retour", "return:1:refund:1", first.Value.ReturnId, first.Value.ExternalRefundId, DateTime.UtcNow);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Id.Should().Be(first.Value.Id);
        payment.Refunds.Should().HaveCount(1);
    }

    [Fact]
    public void Un_remboursement_partiel_ne_clot_pas_le_paiement()
    {
        var payment = CapturedPayment(100m);
        var refund = payment.BeginRefund(Money.Create(40m, "XOF").Value, "retour partiel", "partial", null, null, DateTime.UtcNow).Value;

        payment.MarkRefundSucceeded(refund.Id, "rf_1", DateTime.UtcNow).IsSuccess.Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Captured);
        payment.RefundedAmount.Should().Be(40m);
        payment.RefundableAmount.Should().Be(60m);
    }

    [Fact]
    public void Un_remboursement_superieur_au_restant_est_refuse()
    {
        var payment = CapturedPayment(100m);
        var refund = payment.BeginRefund(Money.Create(80m, "XOF").Value, "retour", "first", null, null, DateTime.UtcNow).Value;
        payment.MarkRefundSucceeded(refund.Id, "rf_1", DateTime.UtcNow);

        var overflow = payment.BeginRefund(Money.Create(30m, "XOF").Value, "retour", "second", null, null, DateTime.UtcNow);

        overflow.IsFailure.Should().BeTrue();
        overflow.Error.Code.Should().Be("payments.refund_amount_exceeds_remaining");
    }

    [Fact]
    public void Un_rejeu_en_processing_ne_cree_pas_de_nouvelle_tentative()
    {
        var payment = CapturedPayment(100m);
        var amount = Money.Create(100m, "XOF").Value;

        var first = payment.BeginRefund(amount, "retour", "same-key", null, null, DateTime.UtcNow);
        var replay = payment.BeginRefund(amount, "retour", "same-key", null, null, DateTime.UtcNow);

        replay.IsSuccess.Should().BeTrue();
        replay.Value.Id.Should().Be(first.Value.Id);
        replay.Value.AttemptCount.Should().Be(1);
        payment.Refunds.Should().HaveCount(1);
    }

    private static Payment CapturedPayment(decimal amount)
    {
        var payment = Payment.Create(
            Guid.NewGuid(),
            PaymentOrderType.Marketplace,
            Guid.NewGuid(),
            Money.Create(amount, "XOF").Value,
            PaymentMethod.Card,
            "Stripe",
            PaymentFlow.PaymentIntent).Value;

        payment.Capture("pi_test").IsSuccess.Should().BeTrue();
        return payment;
    }
}
