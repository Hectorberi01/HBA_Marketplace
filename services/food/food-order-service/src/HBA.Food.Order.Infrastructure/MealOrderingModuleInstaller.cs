using System.Reflection;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Abstractions;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.FoodOrders.Application.Orders.EventHandlers;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.FoodOrders.Contracts;
using HBA.FoodOrders.Domain.Orders;
using HBA.FoodOrders.Domain.Orders.Events;
using HBA.FoodOrders.Infrastructure.Persistence;
using HBA.FoodOrders.Infrastructure.Public;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodOrders.Infrastructure.Caching.Redis;
using HBA.FoodOrders.Infrastructure.Observability;
namespace HBA.FoodOrders.Infrastructure;

/// <summary>
/// Enregistre le module FoodOrders : DbContext, dépôt, API publique, gestionnaires
/// d'événements, validateurs, outbox.
/// </summary>
public sealed class MealOrderingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "FoodOrders";

    public Assembly ApplicationAssembly => typeof(PlaceMealOrderCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheFoodOrder(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteFoodOrder(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFoodOrder()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<MealOrderingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MealOrderingDbContext.SchemaName)));

        services.AddScoped<IMealOrderUnitOfWork>(sp => sp.GetRequiredService<MealOrderingDbContext>());

        services.AddScoped<IMealOrderRepository, MealOrderRepository>();

        // Cinq gestionnaires d'intégration écoutent ici le paiement et la cuisine.
        services.AddScoped<IMealOrderModuleApi, MealOrderModuleApi>();

        // ── Ce que la commande annonce ──────────────────────────────────────
        services.AddScoped<
            IDomainEventHandler<MealOrderPlacedDomainEvent>, MealOrderPlacedDomainEventHandler>();
        services.AddScoped<
            IDomainEventHandler<MealOrderConfirmedDomainEvent>, MealOrderConfirmedDomainEventHandler>();
        services.AddScoped<
            IDomainEventHandler<MealOrderCancelledDomainEvent>, MealOrderCancelledDomainEventHandler>();
        services.AddScoped<
            IDomainEventHandler<MealOrderDeliveredDomainEvent>, MealOrderDeliveredDomainEventHandler>();

        // LA SORTIE DE SECOURS DOIT SE VOIR HORS DU SERVICE.
        services.AddScoped<
            IDomainEventHandler<MealOrderUnderReviewDomainEvent>, MealOrderUnderReviewDomainEventHandler>();
        services.AddScoped<
            IDomainEventHandler<MealOrderResumedAfterReviewDomainEvent>,
            MealOrderResumedAfterReviewDomainEventHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
