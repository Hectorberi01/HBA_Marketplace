using ContractMedia = HBA.Media.Contracts.MediaView;
using ContractVariant = HBA.Media.Contracts.MediaVariantView;
using Google.Protobuf.WellKnownTypes;

using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.Mappers;
using HBA.Media.Contracts;
using HBA.Media.Grpc.V1;

using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoMedia = HBA.Media.Grpc.V1.MediaView;
using ProtoVariant = HBA.Media.Grpc.V1.MediaVariantView;


using ContratsMedia = HBA.Media.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.Media.Contracts.Grpc` (lot D — dissolution des assemblages de
// contrats).

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.Clients;

/// <summary>Côté CLIENT : implémente <see cref="IMediaModuleApi"/> par gRPC.</summary>
internal sealed class MediaGrpcClient : IMediaModuleApi
{
    private readonly MediaApi.MediaApiClient _client;

    public MediaGrpcClient(MediaApi.MediaApiClient client) => _client = client;

    public async Task<ContratsMedia.MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAsync(
            new GetMediaRequest { MediaId = mediaId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? response.Media.ToContract() : null;
    }

    public async Task<IReadOnlyList<ContratsMedia.MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        // Un lot vide ne justifie pas un aller-retour réseau.
        if (mediaIds.Count == 0)
        {
            return [];
        }

        var request = new GetManyMediaRequest();
        request.MediaIds.AddRange(mediaIds.Select(id => id.ToString()));

        var response = await _client.GetManyAsync(request, cancellationToken: cancellationToken);

        return response.Items.Select(item => item.ToContract()).ToList();
    }

    public async Task<IReadOnlyList<ContratsMedia.MediaView>> ListByOwnerAsync(
        string ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListByOwnerAsync(
            new ListByOwnerRequest { OwnerType = ownerType, OwnerId = ownerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Items.Select(item => item.ToContract()).ToList();
    }

    public async Task<SignedMediaUrl?> CreateSignedUrlAsync(
        Guid mediaId, int expiresSeconds = 300, CancellationToken cancellationToken = default)
    {
        var response = await _client.CreateSignedUrlAsync(
            new CreateSignedUrlRequest { MediaId = mediaId.ToString(), ExpiresSeconds = expiresSeconds },
            cancellationToken: cancellationToken);

        return response.Found ? new SignedMediaUrl(response.Url, response.ExpiresInSeconds) : null;
    }
}

internal static class MediaGrpcRegistration
{
    /// <summary>Branche <see cref="IMediaModuleApi"/> sur media-service, en gRPC.</summary>
    public static IServiceCollection AddMediaGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Media"]
            ?? throw new InvalidOperationException(
                "Services:Media est absent — impossible de joindre media-service.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services
            .AddGrpcClient<MediaApi.MediaApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes()
            .ConfigureChannel(channel =>
            {
                // Plafond de taille de réponse.
                channel.MaxReceiveMessageSize = 4 * 1024 * 1024;
            });

        services.AddScoped<IMediaModuleApi, MediaGrpcClient>();

        return services;
    }
}
