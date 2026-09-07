using HBA.Analytics.Infrastructure.Observability.HealthChecks;
using HBA.Analytics.Infrastructure.Observability.Metrics;
using HBA.Shared.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HBA.Analytics.Infrastructure.Observability;

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
/// PAS DE SONDE DE CACHE, PARCE QU'IL N'Y A PAS DE CACHE.
///
/// Les vingt-cinq autres services enregistrent `cache-<module>` ; celui-ci ne le
/// fait pas, et ce n'est pas un oubli. Ses lectures rendent au plus 366 lignes
/// d'une table indexee par sa cle primaire — un cache y ajouterait une
/// invalidation a tenir a jour a chaque evenement consomme, pour une requete qui
/// coute deja peu. Une sonde verte sur un cache qui n'existe pas serait pire
/// qu'aucune sonde : elle affirmerait quelque chose.
///
/// LES TAGS DECIDENT DE CE QUE FAIT L'ORCHESTRATEUR :
///
///   ready         — la base, le courtier. Rouge = ce conteneur ne peut pas
///                   travailler, on le retire du service.
///   dependencies  — les voisins. Jamais rouge : voir `SondeDesDestinationsGrpc`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterObservabiliteAnalytics(
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

        // LE NOM PORTE LE MODULE, ET CE N'EST PAS DECORATIF.
        //
        // Trois hotes montent plusieurs modules dans un seul processus
        // (`HBA.Financial.Api` en monte trois). Deux sondes de meme nom dans le
        // meme conteneur, et `DefaultHealthCheckService` refuse de se construire —
        // au premier `MapHealthChecks`, pas a l'enregistrement, donc la pile
        // d'appel designe le socle et jamais le module fautif.
        //
        // Cet hote-ci n'en monte qu'un ; le suffixe est pose quand meme, parce
        // qu'un module qui rejoindrait un hote compose le ferait sans avoir a
        // renommer ses sondes.
        //
        // Le verdict de l'orchestrateur ne change pas : `/health/ready` agrege
        // sur le TAG `ready`, jamais sur le nom.
        services.AddHealthChecks()
            .AddCheck<SondeDeKafka>("kafka-analytics", tags: ["ready"]);

        // LA SONDE gRPC N'EST PAS DANS `ready` — voir son encadre. Une sonde de
        // disponibilite qui tombe avec un voisin transforme une panne en N pannes.
        services.AddHealthChecks().AddCheck<SondeDesDestinationsGrpc>(
            "grpc-analytics", tags: ["dependencies"]);

        return services;
    }
}
