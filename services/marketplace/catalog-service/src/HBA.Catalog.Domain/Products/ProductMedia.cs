using HBA.Shared.Domain.Primitives;

namespace HBA.Catalog.Domain.Products;

public enum ProductMediaType
{
    Image = 0,
    Video = 1
}

/// <summary>
/// Média rattaché à un produit. Entité enfant de l'agrégat Product : modifiée
/// uniquement via le root.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX CHAMPS POUR UN SEUL FICHIER, ET IL FAUT SAVOIR LEQUEL FAIT FOI.
///
/// <see cref="MediaId"/> EST LA VÉRITÉ. C'est lui qui permet de supprimer le
/// fichier, de régénérer ses variantes, de savoir à qui il appartient. Le service
/// média est le seul propriétaire des octets.
///
/// <see cref="Url"/> EST UNE COPIE DE LECTURE, écrite au moment du dépôt.
///
/// Elle existe pour une raison mesurable : une liste de cinquante produits, une
/// page de résultats de recherche, un panier — tous affichent des vignettes. Ne
/// garder que l'identifiant obligerait chacun de ces écrans à résoudre cinquante
/// médias, sur le chemin de lecture le plus fréquenté du site. C'est le compromis
/// inverse de celui retenu pour Food, où l'on n'affiche qu'un restaurant à la
/// fois.
///
/// CE QUI PEUT LA FAIRE DÉRIVER, ET CE QUI NE LE PEUT PAS.
///
/// Cette URL se dérive du couple (bucket, clé d'objet), tous deux FIXÉS au dépôt
/// et jamais modifiés ensuite. Retraiter un média régénère ses variantes, pas son
/// original : la copie ne bouge donc pas, et aucun événement de runtime n'a à la
/// rafraîchir. Il n'y a pas d'abonnement caché à chercher.
///
/// La seule chose qui l'invalide est un changement d'INFRASTRUCTURE : domaine CDN
/// modifié, bucket public renommé, `PublicBaseUrl` réécrite en configuration. Ce
/// n'est pas un événement — c'est une opération d'exploitation, décidée par un
/// humain, et qui touche d'un coup toutes les images du catalogue.
///
/// `RefreshMediaUrlCommand` existe pour CE cas, et une route d'administration la
/// déclenche produit par produit. Le dire ici évite deux erreurs symétriques :
/// croire que la copie se répare toute seule, et croire qu'elle est immuable.
///
/// LES LIGNES D'AVANT LA BASCULE N'ONT PAS DE MÉDIA.
///
/// Leur `MediaId` vaut zéro et leur `Url` pointe vers l'ancien stockage : pour
/// elles, la copie EST la vérité, et il n'y a rien à rafraîchir ni à supprimer
/// côté service média. C'est ce que <see cref="IsLegacy"/> tranche, et c'est la
/// raison pour laquelle ce prédicat existe ici plutôt que recopié chez chaque
/// appelant.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Le média du service média. Zéro pour une ligne d'avant la bascule.</summary>
    public Guid MediaId { get; private set; }

    /// <summary>
    /// URL publique. Vérité pour une ligne héritée, copie de lecture sinon.
    /// </summary>
    public string Url { get; private set; } = default!;

    public ProductMediaType Type { get; private set; }
    public string AltText { get; private set; } = default!;
    public int Position { get; private set; }
    public bool IsPrimary { get; private set; }

    /// <summary>
    /// TRANSITOIRE : l'identifiant de l'ANCIEN service (hbamediacore).
    ///
    /// Il ne sert plus à rien dans le code courant — mais c'est la seule chose qui
    /// désigne encore les fichiers déposés avant la bascule. L'effacer rendrait
    /// tout nettoyage ultérieur de l'ancien stockage impossible.
    /// </summary>
    public string? LegacyExternalId { get; private set; }

    /// <summary>Cette image est-elle antérieure au service média ?</summary>
    public bool IsLegacy => MediaId == Guid.Empty;

    internal void UnsetPrimary() => IsPrimary = false;

    internal void MakePrimary() => IsPrimary = true;

    internal void SetPosition(int position) => Position = position;

    /// <summary>
    /// Rafraîchit la copie de lecture après un changement d'infrastructure de
    /// stockage — le seul cas où elle peut être devenue fausse.
    ///
    /// NE TOUCHE PAS AUX LIGNES HÉRITÉES : pour elles l'URL est la vérité, et
    /// l'écraser avec celle d'un média qui n'existe pas les rendrait invisibles.
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
