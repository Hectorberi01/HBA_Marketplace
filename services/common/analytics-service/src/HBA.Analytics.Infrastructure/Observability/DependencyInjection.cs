using HBA.Analytics.Infrastructure.Observability.HealthChecks;
using HBA.Analytics.Infrastructure.Observability.Metrics;
using HBA.Shared.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HBA.Analytics.Infrastructure.Observability;

/// <summary>L'OBSERVABILITE DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterObservabiliteAnalytics(
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

        // LE NOM PORTE LE MODULE, ET CE N'EST PAS DECORATIF.
        services.AddHealthChecks()
            .AddCheck<SondeDeKafka>("kafka-analytics", tags: ["ready"]);

        // LA SONDE gRPC N'EST PAS DANS `ready` — voir son encadre.
        services.AddHealthChecks().AddCheck<SondeDesDestinationsGrpc>(
            "grpc-analytics", tags: ["dependencies"]);

        return services;
    }
}
