using ContractUser = HBA.Identity.Contracts.UserSummary;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HBA.Identity.Contracts;
using HBA.Identity.Grpc.V1;
using HBA.Merchants.Infrastructure.Grpc.Mappers;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoUser = HBA.Identity.Grpc.V1.UserSummary;


// COPIE DEPUIS `HBA.Identity.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Merchants.Infrastructure.Grpc.Clients;

/// <summary>Côté client : implémente <see cref="IIdentityModuleApi"/> par gRPC.</summary>
internal sealed class IdentityGrpcClient : IIdentityModuleApi
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

internal static class IdentityGrpcRegistration
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
