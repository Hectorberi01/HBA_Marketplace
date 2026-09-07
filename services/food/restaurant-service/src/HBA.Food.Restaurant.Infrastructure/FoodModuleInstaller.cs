using System.Reflection;
using HBA.Food.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using HBA.Food.Application.Abstractions;
using HBA.Food.Application.Orders;
using HBA.Food.Application.Restaurants;
using HBA.Food.Contracts;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Domain.Orders.Events;
using HBA.Food.Domain.Restaurants.Events;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Staff;
using HBA.Food.Domain.Stations;
using HBA.Shared.Domain.Events;
using HBA.Food.Infrastructure.Persistence;
using HBA.Food.Infrastructure.Public;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Food.Infrastructure.Caching.Redis;
using HBA.Food.Infrastructure.Observability;
namespace HBA.Food.Infrastructure;

/// <summary>ENREGISTREMENT DU MODULE FOOD.</summary>
public sealed class FoodModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Food";

    public Assembly ApplicationAssembly => typeof(RegisterRestaurantCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheFoodRestaurant(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteFoodRestaurant(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFoodRestaurant()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<FoodDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FoodDbContext.SchemaName)));

        services.AddScoped<IFoodUnitOfWork>(sp => sp.GetRequiredService<FoodDbContext>());

        services.AddScoped<IRestaurantRepository, RestaurantRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IMenuCategoryRepository, MenuCategoryRepository>();
        services.AddScoped<IMenuItemRepository, MenuItemRepository>();
        services.AddScoped<IRestaurantStaffRepository, RestaurantStaffRepository>();
        services.AddScoped<IPreparationStationRepository, PreparationStationRepository>();
        services.AddScoped<IFoodOrderRepository, FoodOrderRepository>();

        services.AddScoped<IFoodModuleApi, FoodModuleApi>();

        // CONTRAT DISTINCT DE `IFoodModuleApi`, ET NON UNE COMMODITÉ.
        services.AddScoped<IStorefrontReader, StorefrontReader>();

        // LA GARDE D'IDEMPOTENCE DE CONSOMMATION (§19.5).

        // SANS CET ENREGISTREMENT, L'ÉVÉNEMENT DE VALIDATION MOURAIT DANS L'AGRÉGAT
        // — et le rôle FoodPartner ne serait jamais attribué.
        services.AddScoped<IDomainEventHandler<RestaurantApprovedDomainEvent>, RestaurantApprovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RestaurantRejectedDomainEvent>, RestaurantRejectedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RestaurantSuspendedDomainEvent>, RestaurantSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RestaurantReopenedDomainEvent>, RestaurantReopenedDomainEventHandler>();

        // LES HUIT PUBLICATEURS DE COMMANDE (§19).
        services.AddScoped<IDomainEventHandler<FoodOrderReceivedDomainEvent>, FoodOrderReceivedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderAcceptedDomainEvent>, FoodOrderAcceptedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderRejectedDomainEvent>, FoodOrderRejectedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderPreparationStartedDomainEvent>, FoodOrderPreparationStartedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderReadyForPickupDomainEvent>, FoodOrderReadyForPickupDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderPickedUpDomainEvent>, FoodOrderPickedUpDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderDeliveredDomainEvent>, FoodOrderDeliveredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderCancelledDomainEvent>, FoodOrderCancelledDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
