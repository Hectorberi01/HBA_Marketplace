using HBA.Orders.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Orders.Infrastructure.Grpc.Configuration;
namespace HBA.Orders.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcOrder(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddInventoryGrpcClient(configuration);

        // LE CATALOGUE, POUR REVÉRIFIER LE PRIX AU CHECKOUT (ISSUE-048).
        services.AddProductsGrpcClient(configuration);

        // LE PANIER VIT DANS commerce-service, ET LE CHECKOUT EN DÉPEND.
        services.AddCommerceGrpcClient(configuration);

        // LA COURSE D'UNE COMMANDE MARKETPLACE — LE MAILLON QUI MANQUAIT.
        services.AddDeliveryGrpcClient(configuration);

        // LE DEVIS DE COURSE SE RELIT CHEZ delivery-pricing.
        services.AddDeliveryPricingGrpcClient(configuration);

        // CE CLIENT NE SERT QU'À UNE AUTORISATION.
        services.AddMerchantsGrpcClient(configuration);

        // ORDER-SERVICE DOIT SAVOIR TRADUIRE « FOOD-… », ET SEULEMENT POUR CELA.
        services.AddFoodGrpcClient(configuration);

        return services;
    }
}
