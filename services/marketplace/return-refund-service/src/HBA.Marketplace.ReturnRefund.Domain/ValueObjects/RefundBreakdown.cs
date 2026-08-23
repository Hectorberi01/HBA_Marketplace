namespace HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

public sealed record RefundBreakdown(
    Money Items,
    Money Tax,
    Money OriginalShipping,
    Money DiscountAllocation,
    Money RestockingFee,
    Money ReturnShippingCharge,
    Money PreviousRefunds)
{
    public Money Total()
    {
        var amount = Items.Amount
            + Tax.Amount
            + OriginalShipping.Amount
            - DiscountAllocation.Amount
            - RestockingFee.Amount
            - ReturnShippingCharge.Amount
            - PreviousRefunds.Amount;

        return new Money(Math.Max(0m, decimal.Round(amount, 2)), Items.Currency);
    }
}
