using ContractProfile = HBA.Users.Contracts.UserProfileSummary;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using HBA.Users.Contracts.Grpc;
using HBA.Users.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoProfile = HBA.Users.Grpc.V1.UserProfileSummary;


using HBA.Users.Contracts;
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Users.Contracts.Grpc` (lot B de la migration gRPC).
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

namespace HBA.Users.Api.Grpc.Services;

/// <summary>Côté serveur : expose <see cref="IUsersModuleApi"/> en gRPC.</summary>
public sealed class UsersGrpcService : UsersApi.UsersApiBase
{
    private readonly IUsersModuleApi _users;

    public UsersGrpcService(IUsersModuleApi users) => _users = users;

    public override async Task<GetProfileResponse> GetProfile(
        GetProfileRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        var profile = await _users.GetProfileAsync(userId, context.CancellationToken);

        return profile is null
            ? new GetProfileResponse { Found = false }
            : new GetProfileResponse { Found = true, Profile = profile.ToProto() };
    }

    public override async Task<GetProfilesResponse> GetProfiles(
        GetProfilesRequest request, ServerCallContext context)
    {
        var ids = new List<Guid>(request.UserIds.Count);

        foreach (var raw in request.UserIds)
        {
            if (!Guid.TryParse(raw, out var id))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "user_ids contient un GUID invalide."));
            }

            ids.Add(id);
        }

        var profiles = await _users.GetProfilesAsync(ids, context.CancellationToken);

        var response = new GetProfilesResponse();

        // Les identifiants inconnus sont simplement absents de la carte : c'est
        // le contrat de `GetProfilesAsync`, et le respecter ici évite qu'un
        // appelant croie à une erreur en recevant sept profils sur dix demandés.
        foreach (var (id, profile) in profiles)
        {
            response.Profiles[id.ToString()] = profile.ToProto();
        }

        return response;
    }
}
