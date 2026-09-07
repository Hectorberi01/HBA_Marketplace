using HBA.Inventory.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Inventory.Infrastructure.Grpc.Configuration;
namespace HBA.Inventory.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcInventory(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // SANS CE CLIENT, LES ÉCRITURES DE STOCK RESTENT FERMÉES AU VENDEUR.
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
