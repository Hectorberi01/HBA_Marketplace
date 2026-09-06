using HBA.FoodOrders.Infrastructure.Observability.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Shared.Application.Observability;
using HBA.FoodOrders.Infrastructure.Observability.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace HBA.FoodOrders.Infrastructure.Observability;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'OBSERVABILITE DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// CE QUI EST ICI : ce que ce service expose de lui-meme — ses sondes, ses
/// metriques, sa source d'activite.
///
/// CE QUI RESTE AU SOCLE : le pipeline OpenTelemetry (`AddHbaTelemetry`) et la
/// sonde de base de donnees, posee par `AddHbaService` avec le `TDbContext` de
/// l'hote. Deux configurations d'exportateur seraient deux formats de trace pour
/// un meme collecteur.
///
/// LES TAGS DECIDENT DE CE QUE FAIT L'ORCHESTRATEUR :
///
///   ready         — la base, le cache, le courtier. Rouge = ce conteneur ne peut
///                   pas travailler, on le retire du service.
///   dependencies  — les voisins. Jamais rouge : voir `SondeDesDestinationsGrpc`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterObservabiliteFoodOrder(
        this IServiceCollection services, IConfiguration configuration)
    {
        // LES METRIQUES NEUTRES DE CE SERVICE.
        //
        // `TryAdd` : un service qui compte vraiment quelque chose enregistre SON
        // implementation avant d'appeler ce module, et elle gagne. Sans `TryAdd`,
        // le neutre l'ecraserait — et un compteur muet ne se voit qu'en cherchant
        // un chiffre qui n'arrive jamais.
        services.TryAddSingleton<IPaymentMetrics, NoOpPaymentMetrics>();
        services.TryAddSingleton<IHbaBusinessMetrics, NoOpBusinessMetrics>();
        services.TryAddSingleton<ISecurityMetrics, NoOpSecurityMetrics>();
        services.TryAddSingleton<IOutboxMetrics, NoOpOutboxMetrics>();

        // SINGLETON, ET C'EST LA CONDITION POUR QUE L'ENCADRE DE LA SONDE SOIT VRAI.
        //
        // `AddCheck<T>` resout par `GetServiceOrCreateInstance` : sans cet
        // enregistrement, une NOUVELLE sonde serait construite a chaque appel de
        // l'orchestrateur — donc un `IAdminClient` et une connexion au courtier
        // toutes les quelques secondes. La sonde couterait alors plus que ce
        // qu'elle mesure.
        services.AddSingleton<SondeDeKafka>();

        services.AddHealthChecks()
            .AddCheck<SondeDuCache>("cache", tags: ["ready"])
            .AddCheck<SondeDeKafka>("kafka", tags: ["ready"]);

        // LA SONDE gRPC N'EST PAS DANS `ready` — voir son encadre. Une sonde de
        // disponibilite qui tombe avec un voisin transforme une panne en N pannes.
        // LES METRIQUES NEUTRES DE CE SERVICE.
        //
        // `TryAdd` : un service qui compte vraiment quelque chose enregistre SON
        // implementation avant d'appeler ce module, et elle gagne. Sans `TryAdd`,
        // le neutre l'ecraserait — et un compteur muet ne se voit qu'en cherchant
        // un chiffre qui n'arrive jamais.
        services.TryAddSingleton<IPaymentMetrics, NoOpPaymentMetrics>();
        services.TryAddSingleton<IHbaBusinessMetrics, NoOpBusinessMetrics>();
        services.TryAddSingleton<ISecurityMetrics, NoOpSecurityMetrics>();
        services.TryAddSingleton<IOutboxMetrics, NoOpOutboxMetrics>();

        // SINGLETON, ET C'EST LA CONDITION POUR QUE L'ENCADRE DE LA SONDE SOIT VRAI.
        //
        // `AddCheck<T>` resout par `GetServiceOrCreateInstance` : sans cet
        // enregistrement, une NOUVELLE sonde serait construite a chaque appel de
        // l'orchestrateur — donc un `IAdminClient` et une connexion au courtier
        // toutes les quelques secondes. La sonde couterait alors plus que ce
        // qu'elle mesure.
        services.AddSingleton<SondeDeKafka>();

        services.AddHealthChecks().AddCheck<SondeDesDestinationsGrpc>(
            "grpc", tags: ["dependencies"]);

        return services;
    }
}
