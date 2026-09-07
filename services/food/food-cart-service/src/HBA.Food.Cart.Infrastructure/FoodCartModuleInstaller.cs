using HBA.Shared.Infrastructure.Hosting;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Configuration;
using System.Reflection;
using FluentValidation;
using HBA.FoodCarts.Application.Abstractions;
using HBA.FoodCarts.Application.Carts.Commands;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Consumers;
using HBA.FoodCarts.Contracts;
using HBA.FoodCarts.Domain.Carts;
using HBA.FoodCarts.Domain.Carts.Events;
using HBA.FoodCarts.Infrastructure.Persistence;
using HBA.FoodCarts.Infrastructure.Public;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Pricing.Contracts;
using HBA.Pricing.Promotion;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodCarts.Infrastructure.Caching.Redis;
using HBA.FoodCarts.Infrastructure.Observability;
namespace HBA.FoodCarts.Infrastructure;

/// <summary>
/// Enregistre le module FoodCart : DbContext, repository, API publique,
/// gestionnaires, validateurs, outbox.
/// </summary>
public sealed class FoodCartModuleInstaller : IModuleInstaller
{
    public string ModuleName => "FoodCart";

    public Assembly ApplicationAssembly => typeof(AddItemToFoodCartCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheFoodCart(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteFoodCart(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFoodCart()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<FoodCartDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FoodCartDbContext.SchemaName)));

        services.AddScoped<IFoodCartUnitOfWork>(sp => sp.GetRequiredService<FoodCartDbContext>());

        services.AddScoped<IFoodCartRepository, FoodCartRepository>();

        // Sans cette ligne, le dispatcher tourne SANS garde d'idempotence et se
        // contente de le journaliser : le service resterait rejouable en silence.
        services.AddScoped<IFoodCartModuleApi, FoodCartModuleApi>();
        // LE SEUL BOUCHON DE TARIFICATION QUI SUBSISTE (ISSUE-033).
        services.AddScoped<IPricingModuleApi, PromotionPricingModuleApi>();

        services.AddScoped<
            IDomainEventHandler<FoodCartCheckedOutDomainEvent>, FoodCartCheckedOutDomainEventHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }

    // `GuardNeutralPricing` A ÉTÉ RETIRÉ LE 29 AOÛT 2026, ET C'EST LE BON GESTE.


    /// <summary>Sommes-nous en production ?</summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        return EnvironnementDeploiement.EstProduction(configuration);
    }
}
