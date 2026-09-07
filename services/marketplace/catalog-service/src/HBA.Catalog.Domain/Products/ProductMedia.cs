using HBA.Shared.Domain.Primitives;

namespace HBA.Catalog.Domain.Products;

public enum ProductMediaType
{
    Image = 0,
    Video = 1
}

/// <summary>Média rattaché à un produit.</summary>
public sealed class ProductMedia : Entity<Guid>
{
    private ProductMedia()
    {
    }

    internal ProductMedia(
        Guid id,
        Guid mediaId,
        string url,
        ProductMediaType type,
        string altText,
        int position,
        bool isPrimary,
        string? legacyExternalId)
        : base(id)
    {
        MediaId = mediaId;
        Url = url;
        Type = type;
        AltText = altText;
        Position = position;
        IsPrimary = isPrimary;
        LegacyExternalId = legacyExternalId;
    }

    /// <summary>Le média du service média.</summary>
    public Guid MediaId { get; private set; }

    /// <summary>URL publique. Vérité pour une ligne héritée, copie de lecture sinon.</summary>
    public string Url { get; private set; } = default!;

    public ProductMediaType Type { get; private set; }
    public string AltText { get; private set; } = default!;
    public int Position { get; private set; }
    public bool IsPrimary { get; private set; }

    /// <summary>TRANSITOIRE : l'identifiant de l'ANCIEN service (hbamediacore).</summary>
    public string? LegacyExternalId { get; private set; }

    /// <summary>Cette image est-elle antérieure au service média ?</summary>
    public bool IsLegacy => MediaId == Guid.Empty;

    internal void UnsetPrimary() => IsPrimary = false;

    internal void MakePrimary() => IsPrimary = true;

    internal void SetPosition(int position) => Position = position;

    /// <summary>
    /// Rafraîchit la copie de lecture après un changement d'infrastructure de
    /// stockage — le seul cas où elle peut être devenue fausse.
    /// </summary>
    internal bool RefreshUrl(string url)
    {
        if (IsLegacy || string.IsNullOrWhiteSpace(url) || string.Equals(Url, url.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        Url = url.Trim();
        return true;
    }
}
