using HBA.Commerce.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Commerce.Infrastructure.Grpc.Configuration;
namespace HBA.Commerce.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcCommerce(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddProductsGrpcClient(configuration);

        services.AddInventoryGrpcClient(configuration);

        services.AddOrderingGrpcClient(configuration);

        // SANS CETTE LIGNE, AUCUNE CAMPAGNE COMMERCIALE N'EXISTE (ISSUE-033).
        services.AddPromotionGrpcClient(configuration);

        return services;
    }
}
