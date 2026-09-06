using HBA.Routes.Application;
using HBA.Routes.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

using HBA.Routes.Infrastructure.Caching.Redis;
using Microsoft.Extensions.Configuration;
using HBA.Routes.Infrastructure.Observability;
using HBA.Routes.Infrastructure.Persistence.Outbox;
using HBA.Routes.Infrastructure.Persistence.Inbox;
namespace HBA.Routes.Infrastructure;

public static class RoutesInfrastructureModule
{
    /// <remarks>
    /// `IConfiguration` EST APPARU AVEC LE CACHE DU SERVICE.
    ///
    /// Ce module n'en avait pas besoin — route-service n'a ni base ni cache. Il en
    /// prend un parce que `AjouterCacheDeliveryRoute` lit « Redis:ConnectionString » :
    /// le cache etait branche par le socle pour les vingt-six services, il l'est
    /// desormais par chacun, et ce service n'a pas d'installeur de module ou le
    /// mettre.
    /// </remarks>
    public static IServiceCollection AddRoutesInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterCacheDeliveryRoute(configuration);

        // LES SONDES DE CE SERVICE (Observability/). Jusqu'ici seule la base
        // etait verifiee : un service dont le consommateur Kafka etait mort
        // repondait « ready », et le deploiement individuel le croyait sain.
        services.AjouterObservabiliteDeliveryRoute(configuration);

        // L'outbox et les abonnements sont descendus dans `Messaging/Kafka/`, donc
        // hors de ce module d'infrastructure : ils sont enregistres par
        // `AjouterMessagerieRoute()`, que le composition root peut oublier. Un
        // oubli ne casserait rien de visible. Cette garde, elle, est enregistree
        // ici : elle doit exister quand ce qu'elle verifie est absent.
        services.AddHostedService<GardeDeCablage>();

        services.AddSingleton<RouteStore>();
        // ═════════════════════════════════════════════════════════════════
        // CE SERVICE PUBLIE 3 ÉVÉNEMENTS DANS LE VIDE. ISSUE-007, CRITICAL.
        //
        // `RouteCalculated`, `RouteRecalculated`, `RouteDeliveryEtaUpdated`
        //
        // `IntegrationEventQueue` n'est PAS un publieur : c'est une `List<>`
        // scopée que le `DbContext` du module est censé drainer, dans la même
        // transaction que l'effet métier. Ici il n'y a pas de `DbContext`.
        // Personne ne draine. Les événements sont ajoutés à une liste, la
        // requête se termine, la liste est collectée.
        //
        // LA PERTE EST TOTALE ET SYSTÉMATIQUE, PAS OCCASIONNELLE. Ce n'est
        // pas « on perd les messages au redémarrage » : aucun message n'est
        // jamais parti, même une seule fois. Les heures d'arrivée calculées
        // ne remontent nulle part : le client garde l'estimation initiale,
        // quelle que soit la réalité du terrain.
        //
        // ET RIEN NE LE SIGNALE : `PublishAsync` rend `Task.CompletedTask`.
        // L'appelant voit un succès.
        //
        // POURQUOI CE LOT NE LE CORRIGE PAS.
        //
        // `AddOutboxProcessor<TContext>()` exige `where TContext : DbContext,
        // IOutboxDbContext`. route-service n'a ni `DbContext`, ni table
        // `outbox_messages`, ni migration, ni chaîne de connexion : son état
        // tient dans un `ConcurrentDictionary` (RouteStore). Il n'y a rien à quoi
        // brancher un processeur d'outbox.
        //
        // Fabriquer ici une persistance pour poser l'outbox reviendrait à
        // décider en passant du schéma de ce service — c'est le travail du lot
        // 5.2, qui l'implémente pour de bon (D30). La ligne à ajouter alors,
        // juste ici, tient en un mot :
        //
        //     services.AddOutboxProcessor<RouteDbContext>();
        //
        // Modèles qui fonctionnent : `OrderingModuleInstaller`,
        // `ReturnRefundModuleInstaller`, et — dans ce même univers —
        // `DeliveryPricingInfrastructureModule`, seul service livraison à
        // avoir déjà sa base et son processeur.
        // ═════════════════════════════════════════════════════════════════
        services.AddScoped<IntegrationEventQueue>();
        services.AddScoped<IIntegrationEventPublisher>(sp => sp.GetRequiredService<IntegrationEventQueue>());
        return services;
    }
}
