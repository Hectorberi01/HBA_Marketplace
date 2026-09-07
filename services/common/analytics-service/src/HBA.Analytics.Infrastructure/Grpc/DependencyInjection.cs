using HBA.Analytics.Infrastructure.Grpc.Clients;
using HBA.Analytics.Infrastructure.Grpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcAnalytics(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // SANS CE CLIENT, LE VENDEUR VOIT LES CHIFFRES D'UN AUTRE VENDEUR.
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
