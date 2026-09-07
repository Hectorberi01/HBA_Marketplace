using HBA.Media.Domain.Assets.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Media.Domain.Assets;

public readonly record struct MediaAssetId(Guid Value)
{
    public static MediaAssetId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>UNE VARIANTE À ENREGISTRER — la DESCRIPTION, pas l'entité.</summary>
public sealed record VariantToRecord(
    MediaVariantType Type, string ObjectKey, string ContentType, int Width, int Height, long SizeBytes);

/// <summary>
/// Une représentation dérivée : miniature, format moyen, version recompressée
/// (§12).
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

/// <summary>UN FICHIER DE L'ÉCOSYSTÈME HBA (cahier des charges §4).</summary>
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

    /// <summary>Le nom que l'utilisateur a donné.</summary>
    public string OriginalFileName { get; private set; } = default!;

    /// <summary>Le chemin réel dans le stockage : « products/{id}/{mediaId}.webp ».</summary>
    public string ObjectKey { get; private set; } = default!;

    public string Bucket { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public string Extension { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    public MediaVisibility Visibility { get; private set; }
    public MediaStatus Status { get; private set; }

    /// <summary>SHA-256 du contenu (§8).</summary>
    public string Checksum { get; private set; } = default!;

    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public int? DurationSeconds { get; private set; }

    /// <summary>Le compte qui a téléversé.</summary>
    public Guid CreatedByUserId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>Instant de la suppression LOGIQUE (§19).</summary>
    public DateTime? DeletedOnUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public IReadOnlyCollection<MediaVariant> Variants => _variants.AsReadOnly();

    /// <summary>Le fichier est-il servable ?</summary>
    public bool IsUsable => Status is MediaStatus.Uploaded or MediaStatus.Processing
        or MediaStatus.Ready or MediaStatus.Failed;

    /// <summary>Une URL permanente n'est possible que pour un fichier public.</summary>
    public bool IsPubliclyReadable => Visibility == MediaVisibility.Public && IsUsable;

    // ── Création ────────────────────────────────────────────────────────────

    /// <summary>Enregistre un fichier DÉJÀ déposé dans le stockage.</summary>
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

            // LA VISIBILITÉ VIENT DE LA POLITIQUE, JAMAIS DE L'APPELANT. Une pièce
            // d'identité est privée parce qu'elle est une pièce d'identité — pas
            // parce qu'un développeur y a pensé ce jour-là.
            politique.DefaultVisibility,
            checksum.Trim(),
            createdByUserId);
    }

    /// <summary>La clé de stockage, construite à partir des IDENTIFIANTS (§6).</summary>
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

    /// <summary>Le traitement s'achève : les variantes remplacent celles d'avant.</summary>
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

    /// <summary>Le traitement a échoué.</summary>
    public Result FailProcessing(string reason)
    {
        Status = MediaStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "inconnu" : reason.Trim();
        Touch();

        Raise(new MediaProcessingFailedDomainEvent(Id.Value, OwnerType.ToString(), OwnerId, FailureReason));
        return Result.Success();
    }

    /// <summary>Suppression LOGIQUE (§19).</summary>
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

    /// <summary>Nettoie le nom d'origine avant de le CONSERVER.</summary>
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

    /// <summary>Un média identique a-t-il déjà été déposé par ce propriétaire ?</summary>
    Task<MediaAsset?> FindByChecksumAsync(
        MediaOwnerType ownerType, Guid ownerId, string checksum, CancellationToken cancellationToken = default);

    /// <summary>Les médias supprimés dont la rétention est écoulée.</summary>
    Task<IReadOnlyList<MediaAsset>> ListPurgeableAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default);

    Task AddAsync(MediaAsset asset, CancellationToken cancellationToken = default);

    /// <summary>Effacement DÉFINITIF de la ligne, après effacement des octets.</summary>
    void Remove(MediaAsset asset);
}
