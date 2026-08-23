using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using HBA.Users.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ContractProfile = HBA.Users.Contracts.UserProfileSummary;
using ProtoProfile = HBA.Users.Grpc.V1.UserProfileSummary;

namespace HBA.Users.Contracts.Grpc;

internal static class UsersGrpcMapping
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
