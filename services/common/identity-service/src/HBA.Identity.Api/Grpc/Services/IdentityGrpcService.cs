using ContractUser = HBA.Identity.Contracts.UserSummary;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HBA.Identity.Contracts.Grpc;
using HBA.Identity.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoUser = HBA.Identity.Grpc.V1.UserSummary;


using HBA.Identity.Contracts;
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Identity.Contracts.Grpc` (lot B de la migration gRPC).
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

namespace HBA.Identity.Api.Grpc.Services;

/// <summary>Côté serveur : expose <see cref="IIdentityModuleApi"/> en gRPC.</summary>
public sealed class IdentityGrpcService : IdentityApi.IdentityApiBase
{
    private readonly IIdentityModuleApi _identity;

    public IdentityGrpcService(IIdentityModuleApi identity) => _identity = identity;

    public override async Task<GetUserResponse> GetUser(GetUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        var user = await _identity.GetUserAsync(userId, context.CancellationToken);

        return user is null
            ? new GetUserResponse { Found = false }
            : new GetUserResponse { Found = true, User = user.ToProto() };
    }

    public override async Task<GetUserResponse> GetUserByEmail(
        GetUserByEmailRequest request, ServerCallContext context)
    {
        var user = await _identity.GetUserByEmailAsync(request.Email, context.CancellationToken);

        return user is null
            ? new GetUserResponse { Found = false }
            : new GetUserResponse { Found = true, User = user.ToProto() };
    }

    public override async Task<ValidateAccessTokenResponse> ValidateAccessToken(
        ValidateAccessTokenRequest request, ServerCallContext context)
    {
        var validation = await _identity.ValidateAccessTokenAsync(
            request.AccessToken ?? string.Empty, context.CancellationToken);

        var response = new ValidateAccessTokenResponse
        {
            Valid = validation.Valid,
            UserId = validation.Valid ? validation.UserId.ToString() : string.Empty,
            Reason = validation.Reason ?? string.Empty
        };

        response.Roles.AddRange(validation.Roles);
        response.Permissions.AddRange(validation.Permissions);

        // UN JETON REFUSÉ N'EST PAS UNE ERREUR gRPC.
        //
        // Lever une RpcException sur `valid = false` ferait ouvrir le disjoncteur de
        // l'appelant : quelques centaines de jetons expirés — situation parfaitement
        // normale en fin de session — et le service serait considéré en panne. Un
        // refus est une RÉPONSE, pas un incident.
        return response;
    }

    public override async Task<GetUserRolesResponse> GetUserRoles(
        GetUserRolesRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        var authorization = await _identity.GetUserRolesAsync(userId, context.CancellationToken);

        if (authorization is null)
        {
            return new GetUserRolesResponse { Found = false };
        }

        var response = new GetUserRolesResponse { Found = true };
        response.Roles.AddRange(authorization.Roles);
        response.Permissions.AddRange(authorization.Permissions);

        return response;
    }

    public override async Task<RevokeUserSessionsResponse> RevokeUserSessions(
        RevokeUserSessionsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        var revoked = await _identity.RevokeUserSessionsAsync(userId, context.CancellationToken);

        return new RevokeUserSessionsResponse { Revoked = revoked };
    }
}
