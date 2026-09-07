using ContractProfile = HBA.Users.Contracts.UserProfileSummary;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using HBA.Users.Api.Grpc.Mappers;
using HBA.Users.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoProfile = HBA.Users.Grpc.V1.UserProfileSummary;


using HBA.Users.Contracts;
// DEPLACE DEPUIS `HBA.Users.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.Users.Api.Grpc.Services;

/// <summary>Côté serveur : expose <see cref="IUsersModuleApi"/> en gRPC.</summary>
internal sealed class UsersGrpcService : UsersApi.UsersApiBase
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

        // Les identifiants inconnus sont simplement absents de la carte : c'est le
        // contrat de `GetProfilesAsync`, et le respecter ici évite qu'un appelant
        // croie à une erreur en recevant sept profils sur dix demandés.
        foreach (var (id, profile) in profiles)
        {
            response.Profiles[id.ToString()] = profile.ToProto();
        }

        return response;
    }
}
