using System.Reflection;
using FluentValidation;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Abstractions;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.FoodOrders.Application.Orders.EventHandlers;
using HBA.FoodOrders.Contracts;
using HBA.FoodOrders.Domain.Orders;
using HBA.FoodOrders.Domain.Orders.Events;
using HBA.FoodOrders.Infrastructure.Persistence;
using HBA.FoodOrders.Infrastructure.Public;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.FoodOrders.Infrastructure;

/// <summary>
/// Enregistre le module FoodOrders : DbContext, dépôt, API publique,
/// gestionnaires d'événements, validateurs, outbox.
/// </summary>
public sealed class MealOrderingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "FoodOrders";

    public Assembly ApplicationAssembly => typeof(PlaceMealOrderCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<MealOrderingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MealOrderingDbContext.SchemaName)));

        services.AddScoped<IMealOrderUnitOfWork>(sp => sp.GetRequiredService<MealOrderingDbContext>());

        services.AddScoped<IMealOrderRepository, MealOrderRepository>();

        // Cinq gestionnaires d'intégration écoutent ici le paiement et la cuisine.
        // Sans cette ligne, le dispatcher les appelle SANS garde d'idempotence et
        // se contente de le journaliser.
        services.AddScoped<IConsumerInbox, EfConsumerInbox<MealOrderingDbContext>>();
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
        //
        // Sans ces deux lignes, la commande changerait d'état en silence : le
        // dossier d'arbitrage s'ouvrirait dans la base et le client continuerait
        // d'attendre son repas, sans le moindre message. C'est exactement le
        // silence que cette transition existe pour rompre.
        services.AddScoped<
            IDomainEventHandler<MealOrderUnderReviewDomainEvent>, MealOrderUnderReviewDomainEventHandler>();
        services.AddScoped<
            IDomainEventHandler<MealOrderResumedAfterReviewDomainEvent>,
            MealOrderResumedAfterReviewDomainEventHandler>();

        // ── Ce que la commande écoute : le paiement ─────────────────────────
        services.AddScoped<
            IIntegrationEventHandler<PaymentCapturedIntegrationEvent>,
            ConfirmMealOrderOnPaymentCapturedHandler>();
        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            CancelMealOrderOnPaymentFailedHandler>();

        // ── Ce que la commande écoute : la cuisine ──────────────────────────
        //
        // Refus et annulation amènent au même endroit par deux chemins distincts
        // — voir `KitchenOutcomeHandlers`. La remise du repas est celle qui
        // manquait le plus : sans elle, une commande de repas ne se terminait
        // JAMAIS, l'escrow n'était pas levé, et le restaurateur n'était pas payé.
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>,
            CancelMealOrderOnKitchenRejectionHandler>();
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>,
            CancelMealOrderOnKitchenCancellationHandler>();
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>,
            MarkMealOrderDeliveredOnKitchenDeliveryHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LA PORTE D'ENTRÉE DE L'ARBITRAGE, QUI N'EXISTAIT PAS (ISSUE-061).
        //
        // `PutMealOrderUnderReviewCommand`, son gestionnaire, `MarkUnderReview`
        // et ses quatre gardes, la colonne `ReviewReason` et son index partiel :
        // tout était écrit, et RIEN n'envoyait jamais cette commande. Les deux
        // routes d'administration qui SORTENT de l'arbitrage répondaient donc 409
        // à tous les coups. Voir `HoldMealOrderOnDeliveryCancelledHandler`.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>,
            HoldMealOrderOnDeliveryCancelledHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<MealOrderingDbContext>();
    }
}
