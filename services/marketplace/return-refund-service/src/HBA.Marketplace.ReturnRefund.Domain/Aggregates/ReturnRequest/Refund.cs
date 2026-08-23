using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Domain.Primitives;
using Money = HBA.Marketplace.ReturnRefund.Domain.ValueObjects.Money;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class Refund : Entity<Guid>
{
    private readonly List<RefundAttempt> _attempts = new();

    private Refund()
    {
    }

    private Refund(Guid id, Guid returnId, Money amount, RefundBreakdown breakdown, string idempotencyKey, DateTime createdAtUtc)
        : base(id)
    {
        ReturnId = returnId;
        Amount = amount.Amount;
        Currency = amount.Currency;
        Breakdown = breakdown;
        IdempotencyKey = idempotencyKey;
        Status = RefundStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ReturnId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "XOF";
    public RefundBreakdown Breakdown { get; private set; } = default!;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public RefundStatus Status { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<RefundAttempt> Attempts => _attempts.AsReadOnly();

    public static Refund Create(Guid returnId, Money amount, RefundBreakdown breakdown, string idempotencyKey, DateTime createdAtUtc)
        => new(Guid.NewGuid(), returnId, amount, breakdown, idempotencyKey, createdAtUtc);

    public void MarkProcessing(DateTime nowUtc)
    {
        Status = RefundStatus.Processing;
        _attempts.Add(new RefundAttempt(Guid.NewGuid(), Id, "payment", Status, null, nowUtc));
    }

    public void MarkSucceeded(string providerRefundId, DateTime nowUtc)
    {
        Status = RefundStatus.Succeeded;
        ProviderRefundId = providerRefundId;
        CompletedAtUtc = nowUtc;
        _attempts.Add(new RefundAttempt(Guid.NewGuid(), Id, "payment", Status, providerRefundId, nowUtc));
    }

    public void MarkFailed(string failure, DateTime nowUtc)
    {
        Status = RefundStatus.Failed;
        _attempts.Add(new RefundAttempt(Guid.NewGuid(), Id, "payment", Status, failure, nowUtc));
    }
}
