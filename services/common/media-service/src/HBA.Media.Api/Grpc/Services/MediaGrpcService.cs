using Grpc.Core;
using HBA.Media.Contracts.Grpc;
using HBA.Media.Grpc.V1;


using HBA.Media.Contracts;
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Media.Contracts.Grpc` (lot B de la migration gRPC).
//
// LE SERVEUR VIVAIT DANS L'ASSEMBLAGE DE CONTRATS, DONC CHEZ TOUS SES
// CONSOMMATEURS. Les dix services qui consomment merchant.proto liaient
// l'implementation de seller-service ; les huit qui consomment order.proto
// liaient celle d'order-service. Aucun ne s'en servait.
//
// Le serveur est la surface d'UN service : il vit desormais dans son `.Api`.
// L'assemblage de contrats ne porte plus que le stub genere, le client et son
// enregistrement — le lot C descendra ces deux-la chez les appelants.
//
// CE QUE ÇA NE CHANGE PAS : le cablage. `Program.cs` appelle toujours
// `MapInternalGrpcService<...>()`, avec la meme autorisation et les memes
// intercepteurs. Un deplacement de fichier ne rend rien plus sur.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Media.Api.Grpc.Services;

/// <summary>
/// Côté SERVEUR : expose <see cref="IMediaModuleApi"/> sur le port gRPC.
/// </summary>
/// <remarks>
/// CETTE CLASSE NE DÉCIDE DE RIEN.
///
/// Elle traduit, appelle, retraduit. Toute règle ajoutée ici — un contrôle de
/// droit, un filtrage — serait invisible aux appelants en processus du monolithe
/// pendant l'étranglement : le même média serait visible par un chemin et pas par
/// l'autre, selon que l'appelant est déjà extrait ou non.
/// </remarks>
public sealed class MediaGrpcService : MediaApi.MediaApiBase
{
    private readonly IMediaModuleApi _media;

    public MediaGrpcService(IMediaModuleApi media) => _media = media;

    public override async Task<GetMediaResponse> Get(GetMediaRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.MediaId, out var mediaId))
        {
            // `InvalidArgument` ET NON UNE RÉPONSE « non trouvé ».
            //
            // Un identifiant malformé est une faute de l'APPELANT, pas une absence
            // de donnée. Les confondre ferait passer un bug de construction d'URL
            // pour un média supprimé, et personne ne le chercherait.
            throw new RpcException(new Status(StatusCode.InvalidArgument, "media_id n'est pas un GUID."));
        }

        var view = await _media.GetAsync(mediaId, context.CancellationToken);

        return view is null
            ? new GetMediaResponse { Found = false }
            : new GetMediaResponse { Found = true, Media = view.ToProto() };
    }

    public override async Task<MediaList> GetMany(GetManyMediaRequest request, ServerCallContext context)
    {
        var ids = new List<Guid>(request.MediaIds.Count);

        foreach (var raw in request.MediaIds)
        {
            if (!Guid.TryParse(raw, out var id))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "media_ids contient un GUID invalide."));
            }

            ids.Add(id);
        }

        var views = await _media.GetManyAsync(ids, context.CancellationToken);

        var list = new MediaList();
        list.Items.AddRange(views.Select(v => v.ToProto()));
        return list;
    }

    public override async Task<MediaList> ListByOwner(ListByOwnerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.OwnerId, out var ownerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "owner_id n'est pas un GUID."));
        }

        var views = await _media.ListByOwnerAsync(request.OwnerType, ownerId, context.CancellationToken);

        var list = new MediaList();
        list.Items.AddRange(views.Select(v => v.ToProto()));
        return list;
    }

    public override async Task<CreateSignedUrlResponse> CreateSignedUrl(
        CreateSignedUrlRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.MediaId, out var mediaId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "media_id n'est pas un GUID."));
        }

        // `expires_seconds` non renseigné vaut 0 en proto3 : on retombe sur le
        // défaut du contrat plutôt que de demander une URL expirée d'avance.
        var expires = request.ExpiresSeconds > 0 ? request.ExpiresSeconds : 300;

        var signed = await _media.CreateSignedUrlAsync(mediaId, expires, context.CancellationToken);

        return signed is null
            ? new CreateSignedUrlResponse { Found = false }
            : new CreateSignedUrlResponse
            {
                Found = true,
                Url = signed.Url,
                ExpiresInSeconds = signed.ExpiresInSeconds
            };
    }
}
