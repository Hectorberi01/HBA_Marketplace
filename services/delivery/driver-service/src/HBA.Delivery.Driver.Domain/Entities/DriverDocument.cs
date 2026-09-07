using HBA.Delivery.Driver.Domain.Enums;
using HBA.Shared.Domain.Primitives;

namespace HBA.Delivery.Driver.Domain.Entities;

/// <summary>UNE PIÈCE JUSTIFICATIVE.</summary>
public sealed class DriverDocument : Entity<Guid>
{
    private DriverDocument(Guid id, Guid driverId, DriverDocumentType type, string objectKey)
        : base(id)
    {
        DriverId = driverId;
        Type = type;
        ObjectKey = objectKey;
        Status = DriverDocumentStatus.Submitted;
        SubmittedAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private DriverDocument()
    {
        ObjectKey = string.Empty;
    }

    public Guid DriverId { get; private set; }

    public DriverDocumentType Type { get; private set; }

    /// <summary>Clé de l'objet chez media-service.</summary>
    public string ObjectKey { get; private set; }

    public DriverDocumentStatus Status { get; private set; }

    public DateTime SubmittedAtUtc { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

    internal static DriverDocument Submit(Guid driverId, DriverDocumentType type, string objectKey)
        => new(Guid.NewGuid(), driverId, type, objectKey);

    internal void Approve()
    {
        if (Status is DriverDocumentStatus.Approved)
        {
            return;
        }

        Status = DriverDocumentStatus.Approved;
        ReviewedAtUtc = DateTime.UtcNow;
        RejectionReason = null;
    }

    internal void Reject(string? reason)
    {
        Status = DriverDocumentStatus.Rejected;
        ReviewedAtUtc = DateTime.UtcNow;
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
