using HBA.Media.Contracts;
using HBA.Media.Domain.Assets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Media.Application.Assets;

/// <summary>
/// Les médias d'une ressource métier (§14, <c>GET /media?owner=…</c>).
///
/// AUCUN CONTRÔLE DE DROIT ICI. Le §20 le confie au service propriétaire, et
/// cette requête est appelée PAR lui — il a déjà tranché. L'exposer directement à
/// un client sans filtre laisserait n'importe qui lister les pièces légales d'un
/// vendeur en devinant un identifiant.
/// </summary>
public sealed record ListMediaByOwnerQuery(MediaOwnerType OwnerType, Guid OwnerId)
    : IQuery<IReadOnlyList<MediaSummary>>;

/// <summary>
/// Une vue interne, sans URL.
///
/// DISTINCTE DE <c>MediaView</c> DES CONTRATS, ET DÉLIBÉRÉMENT PLUS PAUVRE.
/// Construire une URL demande le stockage ; cette requête ne le connaît pas et n'a
/// pas à le connaître. Les appelants qui veulent des URL passent par
/// <c>IMediaModuleApi</c>, qui les fabrique en tenant compte de la visibilité.
/// </summary>
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUI A LE DROIT DE TOUCHER À CE MÉDIA ?
///
/// CETTE REQUÊTE EXISTE PARCE QUE N'IMPORTE QUEL COMPTE INSCRIT POUVAIT LIRE
/// N'IMPORTE QUELLE PIÈCE KYB.
///
/// Les routes `GET /{id}/download-url` et `DELETE /{id}` étaient bien
/// authentifiées — le groupe entier passe par `MapAuthenticatedGroup` — mais
/// elles ne vérifiaient RIEN au-delà. Un compte quelconque, avec un identifiant
/// de média glané dans une réponse d'API ou deviné, obtenait une URL signée sur
/// une carte d'identité, un registre de commerce ou une preuve de livraison. Et
/// pouvait l'effacer.
///
/// MEDIA NE SAIT PAS CE QU'EST UN PRODUIT NI UN VENDEUR, et ce n'est pas une
/// lacune : le §20 le pose. Le contrôle métier complet — « cet utilisateur a-t-il
/// le droit sur CE vendeur » — appartient au service propriétaire. Ce que Media
/// sait, en revanche, c'est QUI A DÉPOSÉ le fichier et s'il est public. C'est
/// suffisant pour refermer l'accès général, et c'est tout ce que cette requête
/// prétend faire.
///
/// La chaîne complète du §20 reste à construire quand les routes métier
/// porteront les uploads. D'ici là, mieux vaut un contrôle étroit qu'aucun.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record GetMediaAccessQuery(Guid MediaId) : IQuery<MediaAccess>;

/// <param name="CreatedByUserId">Le compte qui a déposé le fichier.</param>
/// <param name="IsPublic">Un média public se signe pour tout le monde : il est déjà lisible.</param>
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
