using HBA.Financial.Payments.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Payments.Infrastructure.Grpc.Configuration;
namespace HBA.Financial.Payments.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcFinancialPayments(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddOrderingGrpcClient(configuration);

        services.AddFoodGrpcClient(configuration);

        // Commandes de repas : lues par `IPayableOrderReader` pour ouvrir leur
        // paiement (lot 6.1).
        services.AddFoodOrdersGrpcClient(configuration);

        services.AddMerchantsGrpcClient(configuration);

        // CE CLIENT NE SERT QU'À UNE AUTORISATION.
        services.AddDeliveryGrpcClient(configuration);

        return services;
    }
}
