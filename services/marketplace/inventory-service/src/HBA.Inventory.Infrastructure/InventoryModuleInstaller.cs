using System.Reflection;
using HBA.Inventory.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Application.Stock.Commands;
using HBA.Inventory.Application.Stock.EventHandlers;
using HBA.Inventory.Contracts;
using HBA.Inventory.Domain.Locations;
using HBA.Inventory.Domain.Stock;
using HBA.Inventory.Domain.Stock.Events;
using HBA.Inventory.Infrastructure.BackgroundJobs;
using HBA.Inventory.Infrastructure.Persistence;
using HBA.Inventory.Infrastructure.Public;

using HBA.Inventory.Infrastructure.Caching.Redis;
using HBA.Inventory.Infrastructure.Observability;
namespace HBA.Inventory.Infrastructure;

/// <summary>
/// Enregistre le module Inventory : DbContext, repositories, API publique,
/// handlers, validators, outbox.
/// </summary>
public sealed class InventoryModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Inventory";

    public Assembly ApplicationAssembly => typeof(ReserveStockCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheInventory(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteInventory(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieInventory()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.SchemaName)));

        services.AddScoped<IInventoryUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());

        services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();

        // Le journal des mouvements (lot 7.3, ISSUE-044).
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IFulfillmentLocationRepository, FulfillmentLocationRepository>();
        services.AddScoped<IInventoryModuleApi, InventoryModuleApi>();

        services.AddScoped<IDomainEventHandler<StockReservedDomainEvent>, StockReservedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<StockDepletedDomainEvent>, StockDepletedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<StockReplenishedDomainEvent>, StockReplenishedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);


        // LE BALAYAGE D'EXPIRATION DES RÉSERVATIONS (ISSUE-031).
        var periode = TimeSpan.FromMinutes(5);
        if (int.TryParse(configuration["Inventory:ReservationSweep:IntervalSeconds"], out var secondes)
            && secondes > 0)
        {
            periode = TimeSpan.FromSeconds(secondes);
        }

        var taillePar = 100;
        if (int.TryParse(configuration["Inventory:ReservationSweep:BatchSize"], out var lot) && lot > 0)
        {
            taillePar = lot;
        }

        services.AddSingleton(new StockReservationSweepOptions(periode, taillePar));
        services.AddHostedService<ExpireStockReservationsWorker>();

        // LA PURGE DES RÉSERVATIONS TERMINÉES (manque connu depuis le lot 3.5).
        var periodePurge = TimeSpan.FromHours(24);
        if (int.TryParse(configuration["Inventory:ReservationPurge:IntervalHours"], out var heures)
            && heures > 0)
        {
            periodePurge = TimeSpan.FromHours(heures);
        }

        var retention = TimeSpan.FromDays(90);
        if (int.TryParse(configuration["Inventory:ReservationPurge:RetentionDays"], out var jours)
            && jours > 0)
        {
            retention = TimeSpan.FromDays(jours);
        }

        var lotPurge = 500;
        if (int.TryParse(configuration["Inventory:ReservationPurge:BatchSize"], out var taille)
            && taille > 0)
        {
            lotPurge = taille;
        }

        services.AddSingleton(new StockReservationPurgeOptions(periodePurge, retention, lotPurge));
        services.AddHostedService<PurgeStockReservationsWorker>();
    }
}
