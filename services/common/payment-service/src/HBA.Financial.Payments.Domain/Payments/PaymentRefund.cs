using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Payments.Domain.Payments;

public sealed class PaymentRefund : Entity<Guid>
{
    private PaymentRefund()
    {
    }

    internal PaymentRefund(
        Guid id,
        PaymentId paymentId,
        Guid? returnId,
        Guid? externalRefundId,
        Money amount,
        string reason,
        string idempotencyKey,
        DateTime requestedAtUtc)
        : base(id)
    {
        PaymentId = paymentId;
        ReturnId = returnId;
        ExternalRefundId = externalRefundId;
        Amount = amount;
        Reason = reason;
        IdempotencyKey = idempotencyKey;
        Status = PaymentRefundStatus.Processing;
        RequestedAtUtc = requestedAtUtc;
        LastAttemptAtUtc = requestedAtUtc;
        AttemptCount = 1;
    }

    public PaymentId PaymentId { get; private set; }
    public Guid? ReturnId { get; private set; }
    public Guid? ExternalRefundId { get; private set; }
    public Money Amount { get; private set; } = default!;
    public string Reason { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public PaymentRefundStatus Status { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public int AttemptCount { get; private set; }

    public void Retry(DateTime nowUtc)
    {
        Status = PaymentRefundStatus.Processing;
        FailureReason = null;
        LastAttemptAtUtc = nowUtc;
        AttemptCount++;
    }

    public void MarkSucceeded(string providerRefundId, DateTime nowUtc)
    {
        Status = PaymentRefundStatus.Succeeded;
        ProviderRefundId = string.IsNullOrWhiteSpace(providerRefundId) ? Id.ToString() : providerRefundId.Trim();
        FailureReason = null;
        CompletedAtUtc = nowUtc;
        LastAttemptAtUtc = nowUtc;
    }

    public void MarkFailed(string reason, DateTime nowUtc)
    {
        Status = PaymentRefundStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Remboursement refuse par le prestataire." : reason.Trim();
        LastAttemptAtUtc = nowUtc;
    }
}
