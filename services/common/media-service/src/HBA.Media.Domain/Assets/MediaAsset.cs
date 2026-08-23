using HBA.Media.Domain.Assets.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Media.Domain.Assets;

public readonly record struct MediaAssetId(Guid Value)
{
    public static MediaAssetId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE VARIANTE À ENREGISTRER — la DESCRIPTION, pas l'entité.
///
/// CE TYPE EXISTE POUR NE PAS OUVRIR LE CONSTRUCTEUR DE <see cref="MediaVariant"/>.
///
/// La couche Application produit et dépose les images dérivées ; elle a donc
/// besoin de décrire ce qu'elle vient d'écrire. La solution facile aurait été de
/// rendre public le constructeur de l'entité — mais une entité qu'on peut
/// construire hors de son agrégat est une entité qu'on peut créer sans passer par
/// <c>CompleteProcessing</c>, donc sans que le média ne devienne jamais « Ready ».
///
/// Un simple record décrit l'intention ; l'agrégat reste seul à fabriquer ses
/// entités. C'est la leçon tirée de <c>FoodOrderItem</c>, où l'on avait ouvert le
/// constructeur faute d'avoir prévu cette porte-ci.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record VariantToRecord(
    MediaVariantType Type, string ObjectKey, string ContentType, int Width, int Height, long SizeBytes);

/// <summary>
/// Une représentation dérivée : miniature, format moyen, version recompressée (§12).
/// </summary>
public sealed class MediaVariant : Entity<Guid>
{
    private MediaVariant()
    {
    }

    internal MediaVariant(
        Guid id, MediaVariantType type, string objectKey, string contentType,
        int width, int height, long sizeBytes)
        : base(id)
    {
        VariantType = type;
        ObjectKey = objectKey;
        ContentType = contentType;
        Width = width;
        Height = height;
        SizeBytes = sizeBytes;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public MediaVariantType VariantType { get; private set; }
    public string ObjectKey { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN FICHIER DE L'ÉCOSYSTÈME HBA (cahier des charges §4).
///
/// CET AGRÉGAT NE CONTIENT AUCUN OCTET.
///
/// C'est la phrase qui fonde tout le service : « PostgreSQL contient uniquement
/// les métadonnées et états. Les fichiers restent dans le stockage objet » (§18).
/// Ce qui vit ici, c'est OÙ est le fichier, CE QU'IL est, QUI peut le voir et
/// DANS QUEL ÉTAT il se trouve. Son sens métier appartient au service
/// propriétaire.
///
/// IL NE RÉFÉRENCE AUCUN MODULE.
///
/// Le couple (OwnerType, OwnerId) suffit. Pas de clé étrangère vers Product, ni
/// vers Restaurant, ni vers Seller — le §1 l'exige, et c'est ce qui permettra
/// d'extraire Media en service autonome sans rien démêler.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CLÉ D'OBJET N'EST JAMAIS LE NOM DU FICHIER UTILISATEUR (§6).
///
/// Le nom d'origine est conservé — c'est celui qu'on réaffiche et qu'on propose
/// au téléchargement — mais la clé de stockage est construite à partir de
/// l'identifiant du média. Un nom utilisateur comme clé, ce sont des collisions,
/// des caractères de contrôle, des « ../ », et un jour un fichier écrit là où
/// personne ne l'attendait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MediaAsset : AggregateRoot<MediaAssetId>
{
    private readonly List<MediaVariant> _variants = new();

    private MediaAsset()
    {
    }

    private MediaAsset(
        MediaAssetId id,
        MediaOwnerType ownerType,
        Guid ownerId,
        MediaType mediaType,
        string originalFileName,
        string objectKey,
        string bucket,
        string contentType,
        string extension,
        long sizeBytes,
        MediaVisibility visibility,
        string checksum,
        Guid createdByUserId)
        : base(id)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        MediaType = mediaType;
        OriginalFileName = originalFileName;
        ObjectKey = objectKey;
        Bucket = bucket;
        ContentType = contentType;
        Extension = extension;
        SizeBytes = sizeBytes;
        Visibility = visibility;
        Checksum = checksum;
        CreatedByUserId = createdByUserId;
        Status = MediaStatus.Uploaded;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public MediaOwnerType OwnerType { get; private set; }

    /// <summary>Simple Guid : aucune clé étrangère, aucune jointure (§1, §5).</summary>
    public Guid OwnerId { get; private set; }

    public MediaType MediaType { get; private set; }

    /// <summary>
    /// Le nom que l'utilisateur a donné. Conservé POUR L'AFFICHAGE, jamais utilisé
    /// comme clé de stockage.
    /// </summary>
    public string OriginalFileName { get; private set; } = default!;

    /// <summary>Le chemin réel dans le stockage : « products/{id}/{mediaId}.webp ».</summary>
    public string ObjectKey { get; private set; } = default!;

    public string Bucket { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public string Extension { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    public MediaVisibility Visibility { get; private set; }
    public MediaStatus Status { get; private set; }

    /// <summary>
    /// SHA-256 du contenu (§8).
    ///
    /// Sert l'INTÉGRITÉ — savoir qu'un octet a bougé — et prépare la
    /// déduplication : deux vendeurs qui téléversent la même photo produisent la
    /// même empreinte, et le jour où l'on voudra ne stocker qu'une copie, elle est
    /// déjà là. La déduplication elle-même est reportée en V2 (§29).
    /// </summary>
    public string Checksum { get; private set; } = default!;

    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public int? DurationSeconds { get; private set; }

    /// <summary>Le compte qui a téléversé. Le §27 en fait un élément d'audit.</summary>
    public Guid CreatedByUserId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>
    /// Instant de la suppression LOGIQUE (§19). Les octets survivent le temps de
    /// la rétention prévue par la politique de la nature du fichier.
    /// </summary>
    public DateTime? DeletedOnUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public IReadOnlyCollection<MediaVariant> Variants => _variants.AsReadOnly();

    /// <summary>
    /// Le fichier est-il servable ?
    ///
    /// « Failed » RESTE SERVABLE, et c'est délibéré : seules les VARIANTES ont
    /// échoué, l'original est intact dans le stockage. Refuser de le servir
    /// perdrait une photo parfaitement valable parce qu'une miniature n'a pas pu
    /// être calculée.
    /// </summary>
    public bool IsUsable => Status is MediaStatus.Uploaded or MediaStatus.Processing
        or MediaStatus.Ready or MediaStatus.Failed;

    /// <summary>Une URL permanente n'est possible que pour un fichier public.</summary>
    public bool IsPubliclyReadable => Visibility == MediaVisibility.Public && IsUsable;

    // ── Création ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enregistre un fichier DÉJÀ déposé dans le stockage.
    ///
    /// L'ORDRE COMPTE : les octets d'abord, la métadonnée ensuite. L'inverse
    /// laisserait une ligne « Uploaded » désignant un objet inexistant, et chaque
    /// lecture échouerait sur un fichier que la base jure présent.
    ///
    /// La contrepartie est un objet orphelin si l'enregistrement échoue — un octet
    /// perdu dans un bucket, que le ménage de rétention ramassera. C'est le bon
    /// sens du compromis : un fichier sans ligne se nettoie, une ligne sans fichier
    /// se voit par une erreur devant l'utilisateur.
    /// </summary>
    public static Result<MediaAsset> Register(
        MediaOwnerType ownerType,
        Guid ownerId,
        MediaType mediaType,
        string originalFileName,
        string bucket,
        string objectKey,
        string contentType,
        long sizeBytes,
        string checksum,
        Guid createdByUserId,
        MediaAssetId? id = null)
    {
        if (ownerId == Guid.Empty)
        {
            return Error.Validation("media.owner_required", "Le média doit désigner un propriétaire.");
        }

        if (string.IsNullOrWhiteSpace(objectKey) || string.IsNullOrWhiteSpace(bucket))
        {
            return Error.Validation("media.key_required", "Clé de stockage manquante.");
        }

        if (string.IsNullOrWhiteSpace(checksum))
        {
            return Error.Validation("media.checksum_required", "Empreinte du fichier manquante.");
        }

        var politique = MediaTypePolicy.For(mediaType);
        var validation = politique.Validate(contentType, originalFileName, sizeBytes);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new MediaAsset(
            id ?? MediaAssetId.New(),
            ownerType,
            ownerId,
            mediaType,
            SafeFileName(originalFileName),
            objectKey,
            bucket,
            contentType.Trim(),
            MediaTypePolicy.ExtensionFor(contentType),
            sizeBytes,

            // LA VISIBILITÉ VIENT DE LA POLITIQUE, JAMAIS DE L'APPELANT.
            // Une pièce d'identité est privée parce qu'elle est une pièce
            // d'identité — pas parce qu'un développeur y a pensé ce jour-là.
            politique.DefaultVisibility,
            checksum.Trim(),
            createdByUserId);
    }

    /// <summary>
    /// La clé de stockage, construite à partir des IDENTIFIANTS (§6).
    ///
    /// Statique et déterministe : elle se recalcule sans charger l'agrégat, ce qui
    /// permet de déposer les octets AVANT de créer la ligne.
    /// </summary>
    public static string BuildObjectKey(MediaType mediaType, Guid ownerId, MediaAssetId mediaId, string contentType)
        => $"{MediaTypePolicy.For(mediaType).KeyPrefix}/{ownerId:N}/{mediaId.Value:N}.{MediaTypePolicy.ExtensionFor(contentType)}";

    // ── Cycle de vie ────────────────────────────────────────────────────────

    /// <summary>Les dimensions, connues après lecture de l'image.</summary>
    public Result SetDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Result.Failure(Error.Validation("media.dimensions_invalid", "Dimensions invalides."));
        }

        Width = width;
        Height = height;
        Touch();
        return Result.Success();
    }

    public Result BeginProcessing()
    {
        if (Status != MediaStatus.Uploaded)
        {
            return Result.Failure(Error.Conflict("media.not_uploaded", "Ce média n'attend pas de traitement."));
        }

        Status = MediaStatus.Processing;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Le traitement s'achève : les variantes remplacent celles d'avant.
    ///
    /// REMPLACEMENT ET NON AJOUT. Un retraitement (§14, <c>/reprocess</c>) qui
    /// empilerait produirait deux miniatures pour une image, et l'affichage
    /// prendrait la première venue.
    /// </summary>
    public Result CompleteProcessing(IReadOnlyList<VariantToRecord> variants)
    {
        if (Status is MediaStatus.Deleted)
        {
            return Result.Failure(Error.Conflict("media.deleted", "Ce média a été supprimé."));
        }

        _variants.Clear();
        _variants.AddRange(variants.Select(v => new MediaVariant(
            Guid.NewGuid(), v.Type, v.ObjectKey, v.ContentType, v.Width, v.Height, v.SizeBytes)));

        Status = MediaStatus.Ready;
        FailureReason = null;
        Touch();

        Raise(new MediaReadyDomainEvent(Id.Value, OwnerType.ToString(), OwnerId, MediaType.ToString(), ObjectKey));
        return Result.Success();
    }

    /// <summary>
    /// Le traitement a échoué.
    ///
    /// CE N'EST PAS UN ÉCHEC D'UPLOAD. L'original est en place et reste
    /// servable — voir <see cref="IsUsable"/>. Ce que l'on perd, ce sont les
    /// miniatures, et le §14 prévoit de relancer.
    /// </summary>
    public Result FailProcessing(string reason)
    {
        Status = MediaStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "inconnu" : reason.Trim();
        Touch();

        Raise(new MediaProcessingFailedDomainEvent(Id.Value, OwnerType.ToString(), OwnerId, FailureReason));
        return Result.Success();
    }

    /// <summary>
    /// Suppression LOGIQUE (§19).
    ///
    /// LES OCTETS NE PARTENT PAS TOUT DE SUITE, et pour les pièces légales
    /// c'est une obligation : « la suppression doit respecter les règles de
    /// rétention du domaine métier et ne doit pas être immédiate par défaut ». Un
    /// vendeur qui retire sa pièce d'identité par erreur, ou un litige qui remonte
    /// trois mois plus tard, ne doivent pas se heurter à un octet effacé la veille.
    /// </summary>
    public Result SoftDelete(DateTime nowUtc)
    {
        if (Status == MediaStatus.Deleted)
        {
            return Result.Success();
        }

        Status = MediaStatus.Deleted;
        DeletedOnUtc = nowUtc;
        Touch();

        Raise(new MediaDeletedDomainEvent(Id.Value, OwnerType.ToString(), OwnerId, MediaType.ToString()));
        return Result.Success();
    }

    /// <summary>
    /// Le délai de rétention est-il écoulé ? C'est la condition de l'effacement
    /// PHYSIQUE, et elle dépend de la nature du fichier — dix ans pour une facture,
    /// trente jours pour une photo produit.
    /// </summary>
    public bool IsPurgeable(DateTime nowUtc)
        => Status == MediaStatus.Deleted
            && DeletedOnUtc is { } supprime
            && nowUtc >= supprime.AddDays(MediaTypePolicy.For(MediaType).RetentionDaysAfterDelete);

    /// <summary>Toutes les clés d'objet à effacer : l'original ET ses dérivées.</summary>
    public IReadOnlyList<string> AllObjectKeys()
        => new[] { ObjectKey }.Concat(_variants.Select(v => v.ObjectKey)).ToList();

    /// <summary>
    /// Nettoie le nom d'origine avant de le CONSERVER.
    ///
    /// Il ne sert jamais de clé, mais il est réaffiché et proposé au
    /// téléchargement — un nom porteur de séparateurs ou de caractères de contrôle
    /// finirait dans un en-tête HTTP.
    /// </summary>
    private static string SafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "fichier";
        }

        var nom = Path.GetFileName(fileName.Trim());
        var propre = new string(nom.Where(c => !char.IsControl(c) && c is not ('/' or '\\')).ToArray());

        return propre.Length is 0 ? "fichier" : propre.Length > 200 ? propre[^200..] : propre;
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux médias.</summary>
public interface IMediaAssetRepository
{
    Task<MediaAsset?> GetByIdAsync(MediaAssetId id, CancellationToken cancellationToken = default);

    /// <summary>Les médias d'une ressource métier, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<MediaAsset>> ListByOwnerAsync(
        MediaOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Un média identique a-t-il déjà été déposé par ce propriétaire ?
    ///
    /// Prépare la déduplication (§29, V2) et sert dès aujourd'hui d'idempotence :
    /// un mobile qui réessaie un upload interrompu ne crée pas un second fichier.
    /// </summary>
    Task<MediaAsset?> FindByChecksumAsync(
        MediaOwnerType ownerType, Guid ownerId, string checksum, CancellationToken cancellationToken = default);

    /// <summary>Les médias supprimés dont la rétention est écoulée. Pour le ménage physique.</summary>
    Task<IReadOnlyList<MediaAsset>> ListPurgeableAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default);

    Task AddAsync(MediaAsset asset, CancellationToken cancellationToken = default);

    /// <summary>Effacement DÉFINITIF de la ligne, après effacement des octets.</summary>
    void Remove(MediaAsset asset);
}
