using HBA.FoodOrders.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodOrders.Infrastructure.Grpc.Configuration;
namespace HBA.FoodOrders.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcFoodOrder(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // LE PANIER VIT DANS food-cart-service, ET LE PASSAGE EN COMMANDE EN
        // DÉPEND.
        services.AddFoodCartsGrpcClient(configuration);

        // La carte et l'appartenance au personnel : le restaurant prend-il encore
        // des commandes, et ce compte y travaille-t-il ?
        services.AddFoodGrpcClient(configuration);

        // LE DEVIS DE COURSE, RELU ET JAMAIS REDEMANDÉ.
        services.AddDeliveryGrpcClient(configuration);

        // LE DEVIS DE COURSE SE RELIT CHEZ delivery-pricing.
        services.AddDeliveryPricingGrpcClient(configuration);

        return services;
    }
}
