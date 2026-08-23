using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

public sealed record Money(decimal Amount, string Currency)
{
    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0m)
        {
            return Error.Validation("return_refund.money.negative", "Le montant ne peut pas etre negatif.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Error.Validation("return_refund.money.currency_invalid", "La devise doit etre un code ISO a 3 lettres.");
        }

        return new Money(decimal.Round(amount, 2), currency.Trim().ToUpperInvariant());
    }

    public static Money Zero(string currency) => new(0m, currency.Trim().ToUpperInvariant());
}
