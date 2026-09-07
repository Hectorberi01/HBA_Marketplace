using HBA.Catalog.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Catalog.Infrastructure.Grpc.Configuration;
namespace HBA.Catalog.Infrastructure.Grpc;

/// <summary>LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche les clients gRPC du service.</summary>
    public static IServiceCollection AjouterClientsGrpcCatalog(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // UNE SEULE LIGNE MANQUAIT POUR FERMER UN IDOR OUVERT DEPUIS L'ORIGINE.
        services.AddMerchantsGrpcClient(configuration);

        // LE MÊME OUBLI SE REJOUAIT AVEC LE MÉDIA.
        services.AddMediaGrpcClient(configuration);

        return services;
    }
}
