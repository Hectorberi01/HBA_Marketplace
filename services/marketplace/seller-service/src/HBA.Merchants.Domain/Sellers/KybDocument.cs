using HBA.Shared.Domain.Primitives;

namespace HBA.Merchants.Domain.Sellers;

/// <summary>
/// Pièce justificative d'entreprise (registre de commerce, identité). Conformité
/// béninoise (cf. dossier, KybDocument). Le binaire est en object storage ; on ne
/// stocke ici que l'URL. Entité enfant de l'agrégat Seller.
/// </summary>
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
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA PIÈCE, PAR RÉFÉRENCE AU SERVICE MÉDIA.
    ///
    /// CE CHAMP CONTENAIT UNE URL, ET C'ÉTAIT UNE FAILLE.
    ///
    /// `AddKybDocumentCommand` prenait un `FileUrl` VENU DU CLIENT. Un vendeur
    /// pouvait donc rattacher à son dossier l'adresse de n'importe quel objet du
    /// bucket privé — y compris la pièce d'identité d'un autre vendeur — puis
    /// demander une URL présignée pour « sa » pièce, que la route lui signait sans
    /// discuter.
    ///
    /// Un identifiant de média ferme la porte : la couche qui rattache vérifie que
    /// le média est de nature `SellerDocument` et qu'il appartient à CE vendeur.
    /// Sellers ne connaît pas le service média — c'est l'appelant qui contrôle,
    /// comme pour le lieu de collecte côté Inventory.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid MediaId { get; private set; }

    /// <summary>
    /// TRANSITOIRE — l'URL d'avant la bascule.
    ///
    /// Une migration ne peut pas fabriquer de `MediaAsset` à partir d'une URL :
    /// ni empreinte, ni taille, ni type réel. Les pièces déjà déposées gardent
    /// donc leur adresse le temps d'être reversées. À SUPPRIMER ensuite.
    /// </summary>
    public string? LegacyFileUrl { get; private set; }

    /// <summary>Vrai tant que la pièce n'a pas été reversée dans le service média.</summary>
    public bool IsLegacy => MediaId == Guid.Empty;
    public DateTime UploadedOnUtc { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }

    internal void MarkVerified() => VerifiedAtUtc = DateTime.UtcNow;
}
