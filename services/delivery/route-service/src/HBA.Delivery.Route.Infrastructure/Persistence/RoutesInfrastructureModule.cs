using HBA.Routes.Application;
using HBA.Routes.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

using HBA.Routes.Infrastructure.Caching.Redis;
using Microsoft.Extensions.Configuration;
using HBA.Routes.Infrastructure.Observability;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Routes.Infrastructure;

public static class RoutesInfrastructureModule
{
    public static IServiceCollection AddRoutesInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterCacheDeliveryRoute(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteDeliveryRoute(configuration);

        // L'outbox et les abonnements sont descendus dans `Messaging/Kafka/`, donc
        // hors de ce module d'infrastructure : ils sont enregistres par
        // `AjouterMessagerieRoute()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddSingleton<RouteStore>();
        // CE SERVICE PUBLIE 3 ÉVÉNEMENTS DANS LE VIDE. ISSUE-007, CRITICAL.
        services.AddScoped<IntegrationEventQueue>();
        services.AddScoped<IIntegrationEventPublisher>(sp => sp.GetRequiredService<IntegrationEventQueue>());
        return services;
    }
}
