using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Delivery.Pricing.Infrastructure;

public static class DeliveryPricingInfrastructureModule
{
    public static IServiceCollection AddDeliveryPricingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<DeliveryPricingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveryPricingDbContext.SchemaName)));

        services.AddScoped<IPricingStore, EfDeliveryPricingStore>();

        // ═════════════════════════════════════════════════════════════════════
        // LA FILE D'ÉVÉNEMENTS N'EST PLUS RÉENREGISTRÉE ICI.
        //
        // Ces deux lignes existaient parce que l'hôte n'appelait pas
        // `AddBuildingBlocksInfrastructure` — elles comblaient une partie du
        // socle absent, mais pas celle qui empêchait le démarrage. L'hôte prend
        // désormais le socle entier, qui pose `IntegrationEventQueue` et
        // `IIntegrationEventPublisher` exactement de la même façon.
        //
        // Les garder empilerait deux descripteurs identiques. `OutboxRegistration`
        // dit pourquoi on s'en abstient : « sans dommage fonctionnel — le dernier
        // gagne — mais c'est un mensonge dans le conteneur ».
        //
        // CONSÉQUENCE À CONNAÎTRE : ce module n'est plus autonome. Un hôte qui
        // l'appellerait sans poser le socle n'aurait ni file d'événements, ni
        // dispatcher de domaine, ni métriques d'outbox — et ne démarrerait pas.
        // ═════════════════════════════════════════════════════════════════════
        services.AddOutboxProcessor<DeliveryPricingDbContext>();
        return services;
    }
}
