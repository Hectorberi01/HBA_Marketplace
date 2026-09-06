using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using HBA.Users.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ContractProfile = HBA.Users.Contracts.UserProfileSummary;
using ProtoProfile = HBA.Users.Grpc.V1.UserProfileSummary;

namespace HBA.Users.Contracts.Grpc;

public static class UsersGrpcMapping
{
    public static ProtoProfile ToProto(this ContractProfile profile)
    {
        var message = new ProtoProfile
        {
            UserId = profile.UserId.ToString(),
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            DisplayName = profile.DisplayName
        };

        if (profile.AvatarUrl is not null)
        {
            message.AvatarUrl = profile.AvatarUrl;
        }

        return message;
    }

    public static ContractProfile ToContract(this ProtoProfile message)
        => new(
            UserId: Guid.Parse(message.UserId),
            FirstName: message.FirstName,
            LastName: message.LastName,
            DisplayName: message.DisplayName,
            AvatarUrl: message.HasAvatarUrl ? message.AvatarUrl : null);
}



/// <summary>Côté client : implémente <see cref="IUsersModuleApi"/> par gRPC.</summary>
public sealed class UsersGrpcClient : IUsersModuleApi
{
    private readonly UsersApi.UsersApiClient _client;

    public UsersGrpcClient(UsersApi.UsersApiClient client) => _client = client;

    public async Task<ContractProfile?> GetProfileAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetProfileAsync(
            new GetProfileRequest { UserId = userId.ToString() }, cancellationToken: cancellationToken);

        return response.Found ? response.Profile.ToContract() : null;
    }

    public async Task<IReadOnlyDictionary<Guid, ContractProfile>> GetProfilesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, ContractProfile>();
        }

        var request = new GetProfilesRequest();
        request.UserIds.AddRange(userIds.Select(id => id.ToString()));

        var response = await _client.GetProfilesAsync(request, cancellationToken: cancellationToken);

        return response.Profiles.ToDictionary(
            pair => Guid.Parse(pair.Key),
            pair => pair.Value.ToContract());
    }
}

public static class UsersGrpcRegistration
{
    public static IServiceCollection AddUsersGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:User"]
            ?? throw new InvalidOperationException("Services:User est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<UsersApi.UsersApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<IUsersModuleApi, UsersGrpcClient>();

        return services;
    }
}
