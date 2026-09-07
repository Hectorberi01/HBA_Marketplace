using HBA.Communication.Notifications.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Communication.Notifications.Infrastructure.Grpc.Configuration;
namespace HBA.Communication.Notifications.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcCommunicationNotifications(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // SANS CES TROIS LIGNES, LE MODULE DÉMARRE ET ÉCHOUE À LA PREMIÈRE
        // NOTIFICATION — PAS AU DÉMARRAGE.
        services.AddProductsGrpcClient(configuration);

        services.AddMerchantsGrpcClient(configuration);

        services.AddIdentityGrpcClient(configuration);

        // CELUI-CI MANQUAIT, ET C'EST LE CONTENEUR QUI L'A DIT — AU DÉMARRAGE.
        services.AddOrderingGrpcClient(configuration);

        // LE SECOND UNIVERS DE COMMANDES. Les quatre notifications de cuisine
        // résolvaient l'acheteur chez order-service seulement : le client d'une
        // commande de repas ne recevait donc AUCUN suivi, et l'échec ne produisait
        // qu'un Warning.
        services.AddFoodOrdersGrpcClient(configuration);

        // POUR PRÉVENIR LE LIVREUR, IL FAUT REMONTER À SON COMPTE.
        services.AddDeliveryGrpcClient(configuration);

        return services;
    }
}
