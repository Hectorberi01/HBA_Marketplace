using HBA.Media.Application.Abstractions;
using HBA.Media.Contracts;
using HBA.Media.Domain.Assets;
using HBA.Media.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Media.Infrastructure.Public;

/// <summary>L'API IN-PROCESS DU SERVICE MÉDIA. Lecture seule.</summary>
internal sealed class MediaModuleApi : IMediaModuleApi
{
    // Bornes de signature, en secondes.
    private const int DureeSignatureMin = 30;
    private const int DureeSignatureMax = 900;

    private readonly MediaDbContext _dbContext;
    private readonly IObjectStorage _storage;

    public MediaModuleApi(MediaDbContext dbContext, IObjectStorage storage)
    {
        _dbContext = dbContext;
        _storage = storage;
    }

    public async Task<MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var id = new MediaAssetId(mediaId);

        var media = await _dbContext.Assets
            .AsNoTracking()
            .Include("_variants")
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return media is null || media.Status == MediaStatus.Deleted ? null : Map(media);
    }

    /// <summary>Plusieurs médias d'un coup.</summary>
    public async Task<IReadOnlyList<MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        if (mediaIds.Count == 0)
        {
            return [];
        }

        var ids = mediaIds.Distinct().Select(i => new MediaAssetId(i)).ToList();

        var medias = await _dbContext.Assets
            .AsNoTracking()
            .Include("_variants")
            .Where(a => ids.Contains(a.Id) && a.Status != MediaStatus.Deleted)
            .ToListAsync(cancellationToken);

        // ON RESPECTE L'ORDRE DEMANDÉ.
        var parId = medias.ToDictionary(m => m.Id.Value);

        return mediaIds
            .Where(parId.ContainsKey)
            .Select(id => Map(parId[id]))
            .ToList();
    }

    public async Task<IReadOnlyList<MediaView>> ListByOwnerAsync(
        string ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<MediaOwnerType>(ownerType, ignoreCase: true, out var type))
        {
            return [];
        }

        var medias = await _dbContext.Assets
            .AsNoTracking()
            .Include("_variants")
            .Where(a => a.OwnerType == type && a.OwnerId == ownerId && a.Status != MediaStatus.Deleted)
            .OrderBy(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        return medias.Select(Map).ToList();
    }

    public async Task<SignedMediaUrl?> CreateSignedUrlAsync(
        Guid mediaId, int expiresSeconds = 300, CancellationToken cancellationToken = default)
    {
        var id = new MediaAssetId(mediaId);

        var media = await _dbContext.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        // UN MÉDIA SUPPRIMÉ NE SE SIGNE PLUS, même si ses octets survivent le temps
        // de la rétention.
        if (media is null || media.Status == MediaStatus.Deleted)
        {
            return null;
        }

        // PLAFOND SERVEUR SUR LA DURÉE, EN DEUXIÈME RIDEAU.
        var duree = Math.Clamp(expiresSeconds, DureeSignatureMin, DureeSignatureMax);

        var url = _storage.CreateSignedGetUrl(media.Bucket, media.ObjectKey, duree);

        return url.IsFailure ? null : new SignedMediaUrl(url.Value, duree);
    }

    private MediaView Map(MediaAsset media) => new(
        media.Id.Value,
        media.OwnerType.ToString(),
        media.OwnerId,
        media.MediaType.ToString(),
        media.OriginalFileName,
        media.ContentType,
        media.SizeBytes,
        media.Visibility.ToString(),
        media.Status.ToString(),
        media.Width,
        media.Height,

        // NULLE SI LE MÉDIA N'EST PAS PUBLIC (§10).
        media.IsPubliclyReadable
            ? _storage.GetPublicUrl(media.Bucket, media.ObjectKey).Value
            : null,

        // Même règle pour les variantes : elles vivent dans le même bucket que leur
        // original, donc sous la même visibilité.
        media.IsPubliclyReadable
            ? media.Variants
                .Select(v => new MediaVariantView(
                    v.VariantType.ToString(),
                    _storage.GetPublicUrl(media.Bucket, v.ObjectKey).Value,
                    v.Width, v.Height, v.SizeBytes))
                .ToList()
            : [],

        media.CreatedOnUtc,

        // LE SEUL CHAMP DE CETTE VUE QUE L'APPELANT N'A PAS CHOISI.
        media.CreatedByUserId);
}
