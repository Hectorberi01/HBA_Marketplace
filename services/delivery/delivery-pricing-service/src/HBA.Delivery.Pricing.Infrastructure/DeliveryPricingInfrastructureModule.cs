using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
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
        services.AddScoped<IntegrationEventQueue>();
        services.AddScoped<IIntegrationEventPublisher>(sp => sp.GetRequiredService<IntegrationEventQueue>());
        services.AddOutboxProcessor<DeliveryPricingDbContext>();
        return services;
    }
}
