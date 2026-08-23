using HBA.Promotion.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Promotions.Contracts.Grpc;

public static class PromotionGrpcRegistration
{
    /// <summary>
    /// Branche <see cref="IPromotionModuleApi"/> sur promotion-service, en gRPC.
    /// </summary>
    /// <remarks>
    /// L'ADRESSE VIENT DE `Services:Promotion`, COMME POUR LA PASSERELLE.
    ///
    /// Une seconde clé de configuration pour la même destination finirait par
    /// diverger : le proxy et les services de commande taperaient sur deux
    /// instances différentes, avec des budgets distincts selon le chemin emprunté.
    /// Seul le PORT change — gRPC écoute ailleurs que REST, faute de TLS pour
    /// négocier le protocole.
    ///
    /// CETTE MÉTHODE LÈVE SI L'ADRESSE MANQUE, ET C'EST VOULU.
    ///
    /// Un service de commande démarré sans savoir joindre promotion refuserait
    /// silencieusement tous les coupons — les clients paieraient le plein tarif
    /// sans que rien ne le signale. Mieux vaut ne pas démarrer.
    /// </remarks>
    public static IServiceCollection AddPromotionGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Promotion"]
            ?? throw new InvalidOperationException(
                "Services:Promotion est absent — impossible de joindre promotion-service.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services
            .AddGrpcClient<PromotionApi.PromotionApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<IPromotionModuleApi, PromotionGrpcClient>();

        return services;
    }
}
