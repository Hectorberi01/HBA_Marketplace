using HBA.Routes.Application;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Routes.Infrastructure;

public static class RoutesInfrastructureModule
{
    public static IServiceCollection AddRoutesInfrastructure(this IServiceCollection services)
    {
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
