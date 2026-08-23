using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Shared.Domain.Primitives;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class ReturnStatusHistory : Entity<Guid>
{
    private ReturnStatusHistory()
    {
    }

    public ReturnStatusHistory(Guid id, Guid returnId, ReturnStatus status, string reason, DateTime occurredAtUtc, Guid? actorId)
        : base(id)
    {
        ReturnId = returnId;
        Status = status;
        Reason = reason;
        OccurredAtUtc = occurredAtUtc;
        ActorId = actorId;
    }

    public Guid ReturnId { get; private set; }
    public ReturnStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? ActorId { get; private set; }
}
