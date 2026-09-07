using System.Reflection;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Pricing.Contracts;
using HBA.Pricing.Promotion;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using HBA.Commerce.Application.Abstractions;
using HBA.Commerce.Application.Carts.Commands.AddItem;
using HBA.Commerce.Application.Carts.EventHandlers;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Commerce.Contracts;
using HBA.Commerce.Domain.Carts;
using HBA.Commerce.Domain.Carts.Events;
using HBA.Commerce.Infrastructure.Persistence;
using HBA.Commerce.Infrastructure.Public;
using HBA.Orders.Contracts.IntegrationEvents;

using HBA.Commerce.Infrastructure.Caching.Redis;
using HBA.Commerce.Infrastructure.Observability;
using HBA.Commerce.Infrastructure.Persistence.Outbox;
using HBA.Commerce.Infrastructure.Persistence.Inbox;
namespace HBA.Commerce.Infrastructure;

/// <summary>
/// Enregistre le module Cart : DbContext, repository, API publique, handlers,
/// validators, outbox.
/// </summary>
public sealed class CartModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Cart";

    public Assembly ApplicationAssembly => typeof(AddItemToCartCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheCommerce(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteCommerce(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieCommerce()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<CartDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CartDbContext.SchemaName)));

        services.AddScoped<ICartUnitOfWork>(sp => sp.GetRequiredService<CartDbContext>());

        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<ICartModuleApi, CartModuleApi>();

        // LA GARDE D'IDEMPOTENCE, POUR LE CONSOMMATEUR CI-DESSOUS ET LES SUIVANTS.

        // CETTE LIGNE ENREGISTRAIT UNE TARIFICATION NEUTRE, ET C'ÉTAIT ISSUE-033
        // (CRITICAL) À ELLE SEULE.
        services.AddScoped<IPricingModuleApi, PromotionPricingModuleApi>();

        services.AddScoped<IDomainEventHandler<CartCheckedOutDomainEvent>, CartCheckedOutDomainEventHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
