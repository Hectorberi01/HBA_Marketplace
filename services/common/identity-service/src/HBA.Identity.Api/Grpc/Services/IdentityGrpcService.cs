using Grpc.Core;
using HBA.Identity.Api.Grpc.Mappers;
using HBA.Identity.Grpc.V1;

using HBA.Identity.Contracts;
// DEPLACE DEPUIS `HBA.Identity.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.Identity.Api.Grpc.Services;

/// <summary>Côté serveur : expose <see cref="IIdentityModuleApi"/> en gRPC.</summary>
internal sealed class IdentityGrpcService : IdentityApi.IdentityApiBase
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
