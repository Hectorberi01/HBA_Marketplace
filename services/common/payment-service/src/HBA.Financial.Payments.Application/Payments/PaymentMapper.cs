using HBA.Financial.Payments.Contracts;
using HBA.Financial.Payments.Domain.Payments;

namespace HBA.Financial.Payments.Application.Payments;

internal static class PaymentMapper
{
    public static PaymentSummary ToSummary(Payment p) => new(
        p.Id.Value,
        p.OrderId,
        p.BuyerId,
        p.Amount.Amount,
        p.Amount.Currency,
        p.Method.ToString(),
        p.Provider,
        p.ProviderReference,
        p.Status.ToString(),
        p.CreatedAtUtc,
        p.CapturedAtUtc);
}
