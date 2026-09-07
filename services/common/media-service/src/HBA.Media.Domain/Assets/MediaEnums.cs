namespace HBA.Media.Domain.Assets;

/// <summary>À quel domaine métier ce fichier appartient (cahier des charges §5).</summary>
public enum MediaOwnerType
{
    Product = 0,
    ProductVariant = 1,
    Store = 2,
    Restaurant = 3,
    MenuItem = 4,
    User = 5,
    Seller = 6,
    Driver = 7,
    Delivery = 8,
    Order = 9
}

/// <summary>La NATURE du fichier (§3).</summary>
public enum MediaType
{
    /// <summary>Images produit, variantes, galerie.</summary>
    ProductImage = 0,

    /// <summary>Logo, bannière, photos boutique.</summary>
    StoreMedia = 1,

    /// <summary>Logo restaurant, couverture, photos de plats.</summary>
    RestaurantMedia = 2,

    /// <summary>Photo de profil. Public.</summary>
    UserAvatar = 3,

    /// <summary>Pièces légales vendeur. PRIVÉ.</summary>
    SellerDocument = 4,

    /// <summary>CNI, permis, assurance, carte grise.</summary>
    DriverDocument = 5,

    /// <summary>Photo de remise, signature, justificatif.</summary>
    DeliveryProof = 6,

    /// <summary>Facture PDF. PRIVÉ.</summary>
    Invoice = 7,

    /// <summary>Pièce jointe diverse. Restreint.</summary>
    Attachment = 8
}

/// <summary>Qui peut lire le fichier (§4, §10).</summary>
public enum MediaVisibility
{
    /// <summary>URL permanente, cache CDN autorisé.</summary>
    Public = 0,

    /// <summary>Bucket privé, URL signée de courte durée UNIQUEMENT.</summary>
    Private = 1,

    /// <summary>
    /// Privé, mais lisible par un cercle métier plus large qu'un seul compte — une
    /// preuve de livraison, vue par le client, le livreur et le support.
    /// </summary>
    Restricted = 2
}

/// <summary>L'ÉTAT DU FICHIER (§4, §19).</summary>
public enum MediaStatus
{
    /// <summary>Les octets sont dans le stockage, les métadonnées enregistrées.</summary>
    Uploaded = 0,

    /// <summary>Génération des variantes en cours (§11).</summary>
    Processing = 1,

    /// <summary>Utilisable. C'est le seul état qu'un service métier doit exposer.</summary>
    Ready = 2,

    /// <summary>Le traitement a échoué. Le fichier ORIGINAL reste lisible.</summary>
    Failed = 3,

    /// <summary>
    /// Supprimé logiquement. Les octets survivent le temps de la rétention (§19).
    /// </summary>
    Deleted = 4
}

/// <summary>Les représentations dérivées d'une image (§12).</summary>
public enum MediaVariantType
{
    /// <summary>200 × 200, recadrée. Listes et vignettes.</summary>
    Thumbnail = 0,

    /// <summary>480 px de large. Écrans mobiles.</summary>
    Small = 1,

    /// <summary>1024 px. Fiche produit.</summary>
    Medium = 2,

    /// <summary>1600 px. Zoom, grand écran.</summary>
    Large = 3,

    /// <summary>Même dimension que l'original, recompressée.</summary>
    Optimized = 4
}
