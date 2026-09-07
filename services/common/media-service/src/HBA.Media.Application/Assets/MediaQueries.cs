using HBA.Media.Contracts;
using HBA.Media.Domain.Assets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Media.Application.Assets;

/// <summary>Les médias d'une ressource métier (§14, <c>GET /media?owner=…</c>).</summary>
public sealed record ListMediaByOwnerQuery(MediaOwnerType OwnerType, Guid OwnerId)
    : IQuery<IReadOnlyList<MediaSummary>>;

/// <summary>Une vue interne, sans URL.</summary>
public sealed record MediaSummary(
    Guid Id,
    string MediaType,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Visibility,
    string Status,
    int? Width,
    int? Height,
    DateTime CreatedOnUtc);

internal sealed class MediaQueryHandler : IQueryHandler<ListMediaByOwnerQuery, IReadOnlyList<MediaSummary>>
{
    private readonly IMediaAssetRepository _assets;

    public MediaQueryHandler(IMediaAssetRepository assets) => _assets = assets;

    public async Task<Result<IReadOnlyList<MediaSummary>>> Handle(
        ListMediaByOwnerQuery query, CancellationToken cancellationToken)
    {
        var medias = await _assets.ListByOwnerAsync(query.OwnerType, query.OwnerId, cancellationToken);

        IReadOnlyList<MediaSummary> vues = medias
            .Select(m => new MediaSummary(
                m.Id.Value,
                m.MediaType.ToString(),
                m.OriginalFileName,
                m.ContentType,
                m.SizeBytes,
                m.Visibility.ToString(),
                m.Status.ToString(),
                m.Width,
                m.Height,
                m.CreatedOnUtc))
            .ToList();

        return Result.Success(vues);
    }
}

/// <summary>QUI A LE DROIT DE TOUCHER À CE MÉDIA ?</summary>
public sealed record GetMediaAccessQuery(Guid MediaId) : IQuery<MediaAccess>;

/// <param name="CreatedByUserId">Le compte qui a déposé le fichier.</param>
/// <param name="IsPublic">
/// Un média public se signe pour tout le monde : il est déjà lisible.
/// </param>
/// <param name="IsDeleted">Un média supprimé ne se signe plus, même pour son déposant.</param>
public sealed record MediaAccess(Guid MediaId, Guid CreatedByUserId, bool IsPublic, bool IsDeleted);

internal sealed class GetMediaAccessQueryHandler : IQueryHandler<GetMediaAccessQuery, MediaAccess>
{
    private static readonly Error Introuvable = Error.NotFound("media.not_found", "Média introuvable.");

    private readonly IMediaAssetRepository _assets;

    public GetMediaAccessQueryHandler(IMediaAssetRepository assets) => _assets = assets;

    public async Task<Result<MediaAccess>> Handle(
        GetMediaAccessQuery query, CancellationToken cancellationToken)
    {
        var media = await _assets.GetByIdAsync(new MediaAssetId(query.MediaId), cancellationToken);

        if (media is null)
        {
            return Introuvable;
        }

        return new MediaAccess(
            media.Id.Value,
            media.CreatedByUserId,
            media.Visibility == MediaVisibility.Public,
            media.Status == MediaStatus.Deleted);
    }
}
