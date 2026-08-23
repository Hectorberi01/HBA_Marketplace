using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Shared.Domain.Primitives;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class RefundAttempt : Entity<Guid>
{
    private RefundAttempt()
    {
    }

    public RefundAttempt(Guid id, Guid refundId, string provider, RefundStatus status, string? providerReference, DateTime attemptedAtUtc)
        : base(id)
    {
        RefundId = refundId;
        Provider = provider;
        Status = status;
        ProviderReference = providerReference;
        AttemptedAtUtc = attemptedAtUtc;
    }

    public Guid RefundId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public RefundStatus Status { get; private set; }
    public string? ProviderReference { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
}
