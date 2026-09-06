using ContractMedia = HBA.Media.Contracts.MediaView;
using ContractVariant = HBA.Media.Contracts.MediaVariantView;
using Google.Protobuf.WellKnownTypes;

using HBA.Media.Contracts;
using HBA.Media.Grpc.V1;
using HBA.Media.Grpc.V1;

using HBA.Merchants.Infrastructure.Grpc.Mappers;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoMedia = HBA.Media.Grpc.V1.MediaView;
using ProtoVariant = HBA.Media.Grpc.V1.MediaVariantView;


// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Media.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 4 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Merchants.Infrastructure.Grpc.Clients;

/// <summary>
/// Côté CLIENT : implémente <see cref="IMediaModuleApi"/> par gRPC.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST CETTE CLASSE QUI PRÉSERVE LES 36 SITES D'APPEL.
///
/// Le code applicatif continue d'écrire `_media.GetAsync(id, ct)` exactement
/// comme dans le monolithe. Le transport change, l'appelant non — et c'est ce
/// qui rend l'extraction réversible : rebrancher l'implémentation en processus
/// se fait par une ligne d'enregistrement DI.
///
/// AUCUNE EXCEPTION gRPC N'EST AVALÉE ICI.
///
/// La tentation est d'attraper `RpcException` et de rendre `null` : l'appelant
/// ne verrait plus la différence entre « ce média n'existe pas » et
/// « media-service est à terre ». Une galerie afficherait alors des images
/// manquantes au lieu d'un message d'indisponibilité, et l'incident resterait
/// invisible. La politique de résilience — délai, disjoncteur — se pose à
/// l'enregistrement du client, pas ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class MediaGrpcClient : IMediaModuleApi
{
    private readonly MediaApi.MediaApiClient _client;

    public MediaGrpcClient(MediaApi.MediaApiClient client) => _client = client;

    public async Task<MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAsync(
            new GetMediaRequest { MediaId = mediaId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? response.Media.ToContract() : null;
    }

    public async Task<IReadOnlyList<MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        // Un lot vide ne justifie pas un aller-retour réseau. Le monolithe rendait
        // une liste vide sans rien faire ; l'appelant ne doit pas payer la
        // différence.
        if (mediaIds.Count == 0)
        {
            return [];
        }

        var request = new GetManyMediaRequest();
        request.MediaIds.AddRange(mediaIds.Select(id => id.ToString()));

        var response = await _client.GetManyAsync(request, cancellationToken: cancellationToken);

        return response.Items.Select(item => item.ToContract()).ToList();
    }

    public async Task<IReadOnlyList<MediaView>> ListByOwnerAsync(
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
    /// <summary>
    /// Branche <see cref="IMediaModuleApi"/> sur media-service, en gRPC.
    /// </summary>
    /// <remarks>
    /// L'ADRESSE VIENT DE `Services:Media`, COMME POUR LA PASSERELLE.
    ///
    /// Une seconde clé de configuration pour la même destination finirait par
    /// diverger : le proxy et le BFF taperaient sur deux instances différentes du
    /// même service, avec des données distinctes selon le chemin emprunté. Seul
    /// le PORT change — gRPC écoute ailleurs que REST, faute de TLS pour
    /// négocier le protocole.
    /// </remarks>
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
                // Plafond de taille de réponse. Le défaut gRPC est déjà de 4 Mo ;
                // on le fixe explicitement pour que la valeur soit lisible ici
                // plutôt que d'être une surprise le jour où une galerie de deux
                // cents médias dépassera.
                //
                // L'ÉCHÉANCE, elle, n'est pas ici : elle se pose par appel et
                // relève de `InternalCallClientInterceptor`, qui l'applique à tous
                // les clients d'un coup.
                channel.MaxReceiveMessageSize = 4 * 1024 * 1024;
            });

        services.AddScoped<IMediaModuleApi, MediaGrpcClient>();

        return services;
    }
}
