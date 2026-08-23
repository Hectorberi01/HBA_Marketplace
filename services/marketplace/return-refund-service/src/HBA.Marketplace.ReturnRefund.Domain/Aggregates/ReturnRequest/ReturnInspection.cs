using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Shared.Domain.Primitives;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class ReturnInspection : Entity<Guid>
{
    private ReturnInspection()
    {
    }

    public ReturnInspection(
        Guid id,
        Guid returnId,
        InspectionCondition condition,
        StockDisposition disposition,
        string notes,
        DateTime inspectedAtUtc,
        Guid? actorId)
        : base(id)
    {
        ReturnId = returnId;
        Condition = condition;
        Disposition = disposition;
        Notes = notes;
        InspectedAtUtc = inspectedAtUtc;
        ActorId = actorId;
    }

    public Guid ReturnId { get; private set; }
    public InspectionCondition Condition { get; private set; }
    public StockDisposition Disposition { get; private set; }
    public string Notes { get; private set; } = string.Empty;
    public DateTime InspectedAtUtc { get; private set; }
    public Guid? ActorId { get; private set; }
}
