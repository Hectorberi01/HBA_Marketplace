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

public static class IdentityGrpcMapping
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
