using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HBA.Identity.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Alias : `UserSummary` existe dans le contrat C# ET dans le proto, et l'espace
// de noms englobant `HBA.Identity.Contracts` gagnerait toute résolution nue.
using ContractUser = HBA.Identity.Contracts.UserSummary;
using ProtoUser = HBA.Identity.Grpc.V1.UserSummary;

namespace HBA.Identity.Contracts.Grpc;

internal static class IdentityGrpcMapping
{
    public static ProtoUser ToProto(this ContractUser user)
    {
        var message = new ProtoUser
        {
            Id = user.Id.ToString(),
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Status = user.Status,
            EmailVerified = user.EmailVerified,
            MfaEnabled = user.MfaEnabled
        };

        message.RoleIds.AddRange(user.RoleIds.Select(id => id.ToString()));

        // N'affecter que si présent : `""` marquerait le champ comme renseigné et
        // vide, ce qui ferait croire à une version de CGU acceptée alors qu'aucune
        // ne l'a été. C'est le client qui compare cette valeur à la sienne.
        if (user.AcceptedTermsVersion is not null)
        {
            message.AcceptedTermsVersion = user.AcceptedTermsVersion;
        }

        if (user.AcceptedTermsOnUtc.HasValue)
        {
            message.AcceptedTermsOnUtc = ToTimestamp(user.AcceptedTermsOnUtc.Value);
        }

        if (user.EmailVerifiedByAdminOnUtc.HasValue)
        {
            message.EmailVerifiedByAdminOnUtc = ToTimestamp(user.EmailVerifiedByAdminOnUtc.Value);
        }

        return message;
    }

    public static ContractUser ToContract(this ProtoUser message)
        => new(
            Id: Guid.Parse(message.Id),
            FirstName: message.FirstName,
            LastName: message.LastName,
            Email: message.Email,
            PhoneNumber: message.PhoneNumber,
            Status: message.Status,
            EmailVerified: message.EmailVerified,
            MfaEnabled: message.MfaEnabled,
            RoleIds: message.RoleIds.Select(Guid.Parse).ToList(),
            AcceptedTermsVersion: message.HasAcceptedTermsVersion ? message.AcceptedTermsVersion : null,
            AcceptedTermsOnUtc: message.AcceptedTermsOnUtc?.ToDateTime(),
            EmailVerifiedByAdminOnUtc: message.EmailVerifiedByAdminOnUtc?.ToDateTime());

    // `Timestamp.FromDateTime` lève si le DateTime n'est pas marqué UTC. EF Core
    // rend des dates en `Unspecified` depuis PostgreSQL : sans ce marquage,
    // l'exception ne surviendrait qu'au premier appel portant une date lue en base.
    private static Timestamp ToTimestamp(DateTime value)
        => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

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

/// <summary>Côté client : implémente <see cref="IIdentityModuleApi"/> par gRPC.</summary>
public sealed class IdentityGrpcClient : IIdentityModuleApi
{
    private readonly IdentityApi.IdentityApiClient _client;

    public IdentityGrpcClient(IdentityApi.IdentityApiClient client) => _client = client;

    public async Task<ContractUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetUserAsync(
            new GetUserRequest { UserId = userId.ToString() }, cancellationToken: cancellationToken);

        return response.Found ? response.User.ToContract() : null;
    }

    public async Task<ContractUser?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetUserByEmailAsync(
            new GetUserByEmailRequest { Email = email }, cancellationToken: cancellationToken);

        return response.Found ? response.User.ToContract() : null;
    }

    public async Task<AccessTokenValidation> ValidateAccessTokenAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        var response = await _client.ValidateAccessTokenAsync(
            new ValidateAccessTokenRequest { AccessToken = accessToken },
            cancellationToken: cancellationToken);

        return new AccessTokenValidation(
            response.Valid,
            Guid.TryParse(response.UserId, out var id) ? id : Guid.Empty,
            response.Roles.ToList(),
            response.Permissions.ToList(),
            string.IsNullOrEmpty(response.Reason) ? null : response.Reason);
    }

    public async Task<UserAuthorization?> GetUserRolesAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetUserRolesAsync(
            new GetUserRolesRequest { UserId = userId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found
            ? new UserAuthorization(userId, response.Roles.ToList(), response.Permissions.ToList())
            : null;
    }

    public async Task<int> RevokeUserSessionsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.RevokeUserSessionsAsync(
            new RevokeUserSessionsRequest { UserId = userId.ToString() },
            cancellationToken: cancellationToken);

        return response.Revoked;
    }
}

public static class IdentityGrpcRegistration
{
    public static IServiceCollection AddIdentityGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Identity"]
            ?? throw new InvalidOperationException("Services:Identity est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<IdentityApi.IdentityApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<IIdentityModuleApi, IdentityGrpcClient>();

        return services;
    }
}
