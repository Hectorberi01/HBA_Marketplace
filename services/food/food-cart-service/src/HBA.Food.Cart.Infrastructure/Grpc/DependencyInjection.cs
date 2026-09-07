using HBA.FoodCarts.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodCarts.Infrastructure.Grpc.Configuration;
namespace HBA.FoodCarts.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcFoodCart(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // La carte du restaurant : c'est elle qui donne le prix, et non le client.
        services.AddFoodGrpcClient(configuration);

        // La commande de repas : « cet acheteur en est-il à sa première ? », sans
        // quoi les promotions « première commande » seraient inapplicables.
        services.AddFoodOrdersGrpcClient(configuration);

        // LES PROMOTIONS — SANS CE CLIENT, LE PANIER DE REPAS N'A PAS DE
        // TARIFICATION.
        services.AddPromotionGrpcClient(configuration);

        return services;
    }
}
