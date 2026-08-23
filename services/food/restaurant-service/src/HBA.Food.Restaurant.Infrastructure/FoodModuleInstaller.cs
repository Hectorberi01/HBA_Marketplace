using System.Reflection;
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
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Food.Infrastructure;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ENREGISTREMENT DU MODULE FOOD.
///
/// Établissements, cartes, articles et options. Le lieu physique reste dans
/// Inventory, la course dans Delivery, le paiement dans Payments.
///
/// CE MODULE NE CONNAÎT AUCUN AUTRE MODULE — pas même leurs Contracts.
///
/// C'est la même règle que pour HBA Delivery, et pour une raison voisine : HBA
/// Food est un produit distinct de la marketplace, et le jour où il faudra
/// l'extraire, une seule référence à Sellers ou Inventory suffirait à le
/// retenir. Les ponts vivent au composition root.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class FoodModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Food";

    public Assembly ApplicationAssembly => typeof(RegisterRestaurantCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

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
        //
        // `IFoodModuleApi` répond à « que les AUTRES modules ont-ils le droit de
        // demander à Food ? » — il a une implémentation gRPC, et toute méthode
        // ajoutée impose une RPC dans le proto. La vitrine répond à « que la
        // surface HTTP de Food a-t-elle besoin de lire ? », et n'a aucune raison
        // de traverser le réseau.
        services.AddScoped<IStorefrontReader, StorefrontReader>();

        // ─────────────────────────────────────────────────────────────────────
        // LA GARDE D'IDEMPOTENCE DE CONSOMMATION (§19.5).
        //
        // Sans elle, `IntegrationEventDispatcher` ne trouve aucune inbox et se
        // contente d'un avertissement : les quatre consommateurs de ce service
        // s'exécutent nus. Kafka livre au moins une fois — un rejeu de
        // `order.confirmed` rouvre un SECOND ticket de cuisine sur la même
        // commande, et le restaurateur prépare deux fois le repas d'un client qui
        // n'a payé qu'une.
        //
        // Lié à `FoodDbContext` À DESSEIN : la trace n'a de valeur que si elle
        // part dans le même `SaveChangesAsync` que le ticket qu'elle protège.
        // ─────────────────────────────────────────────────────────────────────
        services.AddScoped<IConsumerInbox, EfConsumerInbox<FoodDbContext>>();

        // SANS CET ENREGISTREMENT, L'ÉVÉNEMENT DE VALIDATION MOURAIT DANS
        // L'AGRÉGAT — et le rôle FoodPartner ne serait jamais attribué.
        services.AddScoped<IDomainEventHandler<RestaurantApprovedDomainEvent>, RestaurantApprovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RestaurantRejectedDomainEvent>, RestaurantRejectedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RestaurantSuspendedDomainEvent>, RestaurantSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RestaurantReopenedDomainEvent>, RestaurantReopenedDomainEventHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LES HUIT PUBLICATEURS DE COMMANDE (§19).
        //
        // Sans eux, l'outbox du schéma « food » tourne à vide côté commandes :
        // l'agrégat lève ses événements, aucun ne sort du module. Le refus ne
        // remonterait pas à Ordering — un client débité sans repas — et la mise à
        // disposition n'appellerait aucun livreur.
        //
        // LE HUITIÈME — « LIVRÉ » — MANQUAIT, ET AVEC LUI TOUT L'ARGENT DU
        // RESTAURATEUR. Il ferme la chaîne : Food publie « livré », order-service
        // clôt la commande, financial-service lève l'escrow et rend le gain
        // disponible. Tant qu'il manquait, le repas était remis au client et le
        // restaurateur n'était jamais payé.
        //
        // Un enregistrement manquant ici ne casse RIEN à la compilation ni aux
        // tests : il rend juste un message muet. C'est pourquoi ils sont groupés,
        // et pourquoi un test de composition les compte.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IDomainEventHandler<FoodOrderReceivedDomainEvent>, FoodOrderReceivedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderAcceptedDomainEvent>, FoodOrderAcceptedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderRejectedDomainEvent>, FoodOrderRejectedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderPreparationStartedDomainEvent>, FoodOrderPreparationStartedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderReadyForPickupDomainEvent>, FoodOrderReadyForPickupDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderPickedUpDomainEvent>, FoodOrderPickedUpDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderDeliveredDomainEvent>, FoodOrderDeliveredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<FoodOrderCancelledDomainEvent>, FoodOrderCancelledDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<FoodDbContext>();
    }
}
