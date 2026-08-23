using HBA.Delivery.Driver.Domain.Enums;
using HBA.Shared.Domain.Primitives;

namespace HBA.Delivery.Driver.Domain.Entities;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE PIÈCE JUSTIFICATIVE.
///
/// C'ÉTAIT UN `record` SANS APPELANT, ET IL EST DEVENU UNE ENTITÉ.
///
/// Le lot 5.4 l'a conservé en annonçant que « c'est le lot 5.2 qui construira ce
/// domaine pour de bon ». Un `record` à sept positions ne pouvait rien garantir :
/// n'importe quel appelant pouvait fabriquer une pièce déjà « VERIFIED » sans que
/// personne ne l'ait regardée. Les transitions sont désormais des méthodes, et
/// l'agrégat est le seul à pouvoir les appeler.
///
/// LE FICHIER LUI-MÊME N'EST PAS STOCKÉ ICI. `ObjectKey` désigne un objet dans
/// le stockage de media-service. Ce service ne vérifie NI que la clé existe, NI
/// qu'elle appartient au livreur qui la dépose — c'est le même manque que
/// `MediaGrpcClient.ValidateMediaAsync` chez return-refund-service, et il est
/// ouvert pour la même raison : le contrat de validation n'existe pas encore.
/// Concrètement, un livreur peut déposer la clé du permis d'un autre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Clé de l'objet chez media-service. Jamais une URL signée : elles expirent.</summary>
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
