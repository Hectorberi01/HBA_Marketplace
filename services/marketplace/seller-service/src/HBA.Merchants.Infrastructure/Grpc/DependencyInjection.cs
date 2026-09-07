using HBA.Merchants.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Merchants.Infrastructure.Grpc.Configuration;
namespace HBA.Merchants.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcMerchants(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddIdentityGrpcClient(configuration);

        // SANS CETTE LIGNE, N'IMPORTE QUEL MÉDIA DE LA PLATEFORME POUVAIT DEVENIR
        // UNE PIÈCE KYB.
        services.AddMediaGrpcClient(configuration);

        // ET SANS CELLE-CI, UNE BOUTIQUE EXPÉDIAIT DEPUIS L'ADRESSE D'UN
        // CONCURRENT.
        services.AddInventoryGrpcClient(configuration);

        // POUR RECALCULER LE COMPTEUR DE VENTES, PAS POUR L'INCRÉMENTER.
        services.AddOrderingGrpcClient(configuration);

        return services;
    }
}
