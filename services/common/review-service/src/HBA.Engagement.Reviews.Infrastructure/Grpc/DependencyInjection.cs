using HBA.Engagement.Reviews.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Engagement.Reviews.Infrastructure.Grpc.Configuration;
namespace HBA.Engagement.Reviews.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcEngagementReviews(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddOrderingGrpcClient(configuration);

        // LA RÉSOLUTION VENDEUR — SANS ELLE, LA RÉPONSE AUX AVIS RESTE OUVERTE À
        // TOUS.
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
