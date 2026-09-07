using HBA.Merchants.Infrastructure.Observability.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Shared.Application.Observability;
using HBA.Merchants.Infrastructure.Observability.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace HBA.Merchants.Infrastructure.Observability;

/// <summary>L'OBSERVABILITE DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterObservabiliteMerchants(
        this IServiceCollection services, IConfiguration configuration)
    {
        // LES METRIQUES NEUTRES DE CE SERVICE.
        services.TryAddSingleton<IPaymentMetrics, NoOpPaymentMetrics>();
        services.TryAddSingleton<IHbaBusinessMetrics, NoOpBusinessMetrics>();
        services.TryAddSingleton<ISecurityMetrics, NoOpSecurityMetrics>();
        services.TryAddSingleton<IOutboxMetrics, NoOpOutboxMetrics>();

        // SINGLETON, ET C'EST LA CONDITION POUR QUE L'ENCADRE DE LA SONDE SOIT
        // VRAI.
        services.AddSingleton<SondeDeKafka>();

        // LES NOMS PORTENT LE MODULE, ET CE N'EST PAS DECORATIF.
        services.AddHealthChecks()
            .AddCheck<SondeDuCache>("cache-merchants", tags: ["ready"])
            .AddCheck<SondeDeKafka>("kafka-merchants", tags: ["ready"]);

        // LA SONDE gRPC N'EST PAS DANS `ready` — voir son encadre.
        services.AddHealthChecks().AddCheck<SondeDesDestinationsGrpc>(
            "grpc-merchants", tags: ["dependencies"]);

        return services;
    }
}
