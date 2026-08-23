using HBA.Shared.Domain.Primitives;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class ReturnEvidence : Entity<Guid>
{
    private ReturnEvidence()
    {
    }

    public ReturnEvidence(Guid id, Guid returnId, string mediaId, string kind, string? caption, DateTime createdAtUtc)
        : base(id)
    {
        ReturnId = returnId;
        MediaId = mediaId;
        Kind = kind;
        Caption = caption;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ReturnId { get; private set; }
    public string MediaId { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string? Caption { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
