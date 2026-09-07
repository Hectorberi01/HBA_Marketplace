using System.Reflection;
using HBA.Orders.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Application.Orders.Commands.PlaceOrder;
using HBA.Orders.Application.Orders.EventHandlers;
using HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Orders.Contracts;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.Events;
using HBA.Orders.Domain.Orders.SellerOrders;
using HBA.Orders.Domain.Orders.SellerOrders.Events;
using HBA.Orders.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Public;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;

using HBA.Orders.Infrastructure.Caching.Redis;
using HBA.Orders.Infrastructure.Observability;
using HBA.Orders.Infrastructure.Persistence.Outbox;
using HBA.Orders.Infrastructure.Persistence.Inbox;
namespace HBA.Orders.Infrastructure;

/// <summary>
/// Enregistre le module Ordering : DbContext, repository, API publique, Saga
/// handlers, validators, outbox.
/// </summary>
public sealed class OrderingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Ordering";

    public Assembly ApplicationAssembly => typeof(PlaceOrderCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheOrder(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteOrder(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieOrder()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<OrderingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", OrderingDbContext.SchemaName)));

        services.AddScoped<IOrderingUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());

        services.AddScoped<IOrderRepository, OrderRepository>();

        // SANS CETTE LIGNE, LES CINQ ROUTES VENDEUR NE DÉMARRENT PAS.
        services.AddScoped<ISellerOrderRepository, SellerOrderRepository>();

        services.AddScoped<IOrderingModuleApi, OrderingModuleApi>();

        // SANS CETTE LIGNE, LES NEUF CONSOMMATEURS DU SERVICE SONT REJOUABLES.

        services.AddScoped<IDomainEventHandler<OrderPlacedDomainEvent>, OrderPlacedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderConfirmedDomainEvent>, OrderConfirmedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelledDomainEvent>, OrderCancelledDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderDeliveredDomainEvent>, OrderDeliveredDomainEventHandler>();

        // LA SORTIE DE SECOURS DOIT SE VOIR HORS DU MODULE.
        services.AddScoped<IDomainEventHandler<OrderUnderReviewDomainEvent>, OrderUnderReviewDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderResumedAfterReviewDomainEvent>, OrderResumedAfterReviewDomainEventHandler>();

        // LE REFUS D'UN VENDEUR DOIT SORTIR DU MODULE (ISSUE-027).
        services.AddScoped<IDomainEventHandler<SellerOrderRefusedDomainEvent>, SellerOrderRefusedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelledDomainEvent>, CancelSellerOrdersOnOrderCancelledHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
