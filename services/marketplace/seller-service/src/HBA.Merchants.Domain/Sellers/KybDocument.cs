using HBA.Shared.Domain.Primitives;

namespace HBA.Merchants.Domain.Sellers;

/// <summary>Pièce justificative d'entreprise (registre de commerce, identité).</summary>
public sealed class KybDocument : Entity<Guid>
{
    private KybDocument()
    {
    }

    internal KybDocument(Guid id, KybDocumentType type, Guid mediaId)
        : base(id)
    {
        Type = type;
        MediaId = mediaId;
        UploadedOnUtc = DateTime.UtcNow;
    }

    public KybDocumentType Type { get; private set; }
    /// <summary>LA PIÈCE, PAR RÉFÉRENCE AU SERVICE MÉDIA.</summary>
    public Guid MediaId { get; private set; }

    /// <summary>TRANSITOIRE — l'URL d'avant la bascule.</summary>
    public string? LegacyFileUrl { get; private set; }

    /// <summary>Vrai tant que la pièce n'a pas été reversée dans le service média.</summary>
    public bool IsLegacy => MediaId == Guid.Empty;
    public DateTime UploadedOnUtc { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }

    internal void MarkVerified() => VerifiedAtUtc = DateTime.UtcNow;
}
