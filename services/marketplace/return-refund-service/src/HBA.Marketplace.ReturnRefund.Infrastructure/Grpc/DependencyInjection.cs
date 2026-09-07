using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.Configuration;
namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcMarketplaceReturnRefund(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // SANS CE CLIENT, LES ROUTES VENDEUR NE SAVENT PAS À QUI PARLE LE JETON.
        services.AddMerchantsGrpcClient(configuration);

        // LA VÉRIFICATION DES PREUVES PHOTO — SANS CE CLIENT, ELLE N'EXISTE PAS.
        services.AddMediaGrpcClient(configuration);

        services.AddOrderingGrpcClient(configuration);

        services.AddFinancialGrpcClient(configuration);

        return services;
    }
}
