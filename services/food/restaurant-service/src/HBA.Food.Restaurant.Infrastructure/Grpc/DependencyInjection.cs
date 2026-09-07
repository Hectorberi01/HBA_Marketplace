using HBA.Food.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Food.Infrastructure.Grpc.Configuration;
namespace HBA.Food.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcFoodRestaurant(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // LE PARCOURS FOOD, DE LA COMMANDE PAYÉE AU CLIENT SERVI.
        services.AddOrderingGrpcClient(configuration);

        services.AddInventoryGrpcClient(configuration);

        services.AddDeliveryGrpcClient(configuration);

        // LE CINQUIÈME CLIENT — CELUI SANS LEQUEL AUCUN REPAS N'AVAIT DE LIVREUR.
        services.AddFoodOrdersGrpcClient(configuration);

        // LE QUATRIÈME CLIENT — CELUI QUI REND LE RESTAURATEUR PAYABLE.
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
