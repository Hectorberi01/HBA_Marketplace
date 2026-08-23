namespace HBA.Media.Domain.Assets;

/// <summary>
/// À quel domaine métier ce fichier appartient (cahier des charges §5).
///
/// UNE ÉNUMÉRATION, ET AUCUNE CLÉ ÉTRANGÈRE. C'est tout le principe du
/// service : « Media Service ne fait pas de jointure EF avec Product Service ».
/// Le couple (OwnerType, OwnerId) dit à quoi le fichier se rattache sans créer la
/// moindre dépendance de base entre modules — et c'est ce qui permettra d'extraire
/// Media en service autonome sans démêler quoi que ce soit.
/// </summary>
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

/// <summary>
/// La NATURE du fichier (§3). C'est elle qui porte les règles : formats admis,
/// taille maximale, visibilité par défaut, rétention.
///
/// À DISTINGUER DE <see cref="MediaOwnerType"/>. Un même propriétaire porte
/// plusieurs natures — un vendeur a un logo de boutique (public) et des pièces
/// légales (privées). Les fondre aurait rendu impossible d'appliquer deux
/// politiques au même propriétaire.
/// </summary>
public enum MediaType
{
    /// <summary>Images produit, variantes, galerie. Public.</summary>
    ProductImage = 0,

    /// <summary>Logo, bannière, photos boutique. Public.</summary>
    StoreMedia = 1,

    /// <summary>Logo restaurant, couverture, photos de plats. Public.</summary>
    RestaurantMedia = 2,

    /// <summary>Photo de profil. Public.</summary>
    UserAvatar = 3,

    /// <summary>Pièces légales vendeur. PRIVÉ.</summary>
    SellerDocument = 4,

    /// <summary>CNI, permis, assurance, carte grise. PRIVÉ.</summary>
    DriverDocument = 5,

    /// <summary>Photo de remise, signature, justificatif. PRIVÉ.</summary>
    DeliveryProof = 6,

    /// <summary>Facture PDF. PRIVÉ.</summary>
    Invoice = 7,

    /// <summary>Pièce jointe diverse. Restreint.</summary>
    Attachment = 8
}

/// <summary>
/// Qui peut lire le fichier (§4, §10).
///
/// CE N'EST PAS UN DROIT MÉTIER. Le cahier (§20) est explicite : « Media
/// Service connaît la visibilité du fichier, mais les droits métier complets
/// appartiennent au service propriétaire ». Media sait qu'une pièce de livreur est
/// privée ; il ne sait pas si CE demandeur a le droit de la voir.
/// </summary>
public enum MediaVisibility
{
    /// <summary>URL permanente, cache CDN autorisé. Images produit, logos.</summary>
    Public = 0,

    /// <summary>Bucket privé, URL signée de courte durée UNIQUEMENT.</summary>
    Private = 1,

    /// <summary>
    /// Privé, mais lisible par un cercle métier plus large qu'un seul compte —
    /// une preuve de livraison, vue par le client, le livreur et le support.
    ///
    /// Techniquement identique à Private côté stockage : la différence est un
    /// signal pour le service propriétaire, qui décide.
    /// </summary>
    Restricted = 2
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉTAT DU FICHIER (§4, §19).
///
/// DEUX ÉTATS DU CAHIER SONT DÉLIBÉRÉMENT ABSENTS, ET C'EST UN CHOIX MOTIVÉ.
///
/// <c>PendingUpload</c> n'existe que dans le flux « presigned » (§7 mode B), où
/// la métadonnée précède les octets. Le mode retenu ici est l'upload PAR L'API :
/// le fichier est validé et stocké AVANT que l'enregistrement n'existe. Aucun
/// chemin ne pourrait donc produire cet état.
///
/// <c>Quarantined</c> suppose un antivirus (§9), qui n'est pas au périmètre V1
/// (§29) et dont aucun service n'existe dans ce dépôt.
///
/// Les déclarer sans producteur serait exactement le défaut que ce dépôt traque :
/// une valeur d'énumération irréprochable qu'aucun code ne rend jamais, et sur
/// laquelle des écrans finissent par brancher des cas morts. Ils reviendront AVEC
/// le presigned et AVEC l'antivirus, pas avant.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Supprimé logiquement. Les octets survivent le temps de la rétention (§19).</summary>
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
