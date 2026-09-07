using HBA.Promotions.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Promotions.Infrastructure.Grpc.Configuration;
namespace HBA.Promotions.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcPromotions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // SANS CE CLIENT, LES TROIS ROUTES MARCHAND NE SAVENT PAS À QUI PARLE LE
        // JETON.
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
