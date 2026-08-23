using HBA.Media.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Media.Contracts.Grpc;

public static class MediaGrpcRegistration
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
