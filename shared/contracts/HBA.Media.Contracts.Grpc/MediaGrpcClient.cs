using HBA.Media.Grpc.V1;

namespace HBA.Media.Contracts.Grpc;

/// <summary>
/// Côté CLIENT : implémente <see cref="IMediaModuleApi"/> par gRPC.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST CETTE CLASSE QUI PRÉSERVE LES 36 SITES D'APPEL.
///
/// Le code applicatif continue d'écrire `_media.GetAsync(id, ct)` exactement
/// comme dans le monolithe. Le transport change, l'appelant non — et c'est ce
/// qui rend l'extraction réversible : rebrancher l'implémentation en processus
/// se fait par une ligne d'enregistrement DI.
///
/// AUCUNE EXCEPTION gRPC N'EST AVALÉE ICI.
///
/// La tentation est d'attraper `RpcException` et de rendre `null` : l'appelant
/// ne verrait plus la différence entre « ce média n'existe pas » et
/// « media-service est à terre ». Une galerie afficherait alors des images
/// manquantes au lieu d'un message d'indisponibilité, et l'incident resterait
/// invisible. La politique de résilience — délai, disjoncteur — se pose à
/// l'enregistrement du client, pas ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MediaGrpcClient : IMediaModuleApi
{
    private readonly MediaApi.MediaApiClient _client;

    public MediaGrpcClient(MediaApi.MediaApiClient client) => _client = client;

    public async Task<MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAsync(
            new GetMediaRequest { MediaId = mediaId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? response.Media.ToContract() : null;
    }

    public async Task<IReadOnlyList<MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        // Un lot vide ne justifie pas un aller-retour réseau. Le monolithe rendait
        // une liste vide sans rien faire ; l'appelant ne doit pas payer la
        // différence.
        if (mediaIds.Count == 0)
        {
            return [];
        }

        var request = new GetManyMediaRequest();
        request.MediaIds.AddRange(mediaIds.Select(id => id.ToString()));

        var response = await _client.GetManyAsync(request, cancellationToken: cancellationToken);

        return response.Items.Select(item => item.ToContract()).ToList();
    }

    public async Task<IReadOnlyList<MediaView>> ListByOwnerAsync(
        string ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListByOwnerAsync(
            new ListByOwnerRequest { OwnerType = ownerType, OwnerId = ownerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Items.Select(item => item.ToContract()).ToList();
    }

    public async Task<SignedMediaUrl?> CreateSignedUrlAsync(
        Guid mediaId, int expiresSeconds = 300, CancellationToken cancellationToken = default)
    {
        var response = await _client.CreateSignedUrlAsync(
            new CreateSignedUrlRequest { MediaId = mediaId.ToString(), ExpiresSeconds = expiresSeconds },
            cancellationToken: cancellationToken);

        return response.Found ? new SignedMediaUrl(response.Url, response.ExpiresInSeconds) : null;
    }
}
