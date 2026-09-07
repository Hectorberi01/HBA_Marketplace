using HBA.Gateway.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Gateway.Infrastructure.Grpc.Configuration;
namespace HBA.Gateway.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // LÈVE À LA CONSTRUCTION DE L'HÔTE si `Services:Identity` est absente.
        services.AddIdentityGrpcClient(configuration);

        return services;
    }
}
