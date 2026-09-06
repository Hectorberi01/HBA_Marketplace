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
using HBA.Shared.Infrastructure.Inbox;
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

namespace HBA.Commerce.Infrastructure;

/// <summary>Enregistre le module Cart : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class CartModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Cart";

    public Assembly ApplicationAssembly => typeof(AddItemToCartCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc
        // hors de cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieCommerce()`, que le composition root peut oublier.
        // Un oubli ne casserait rien de visible — le service demarre et n'emet
        // plus rien. Cette garde, elle, est enregistree ici : elle doit exister
        // quand ce qu'elle verifie est absent.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<CartDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CartDbContext.SchemaName)));

        services.AddScoped<ICartUnitOfWork>(sp => sp.GetRequiredService<CartDbContext>());

        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<ICartModuleApi, CartModuleApi>();

        // LA GARDE D'IDEMPOTENCE, POUR LE CONSOMMATEUR CI-DESSOUS ET LES SUIVANTS.
        //
        // `IntegrationEventDispatcher` résout `IConsumerInbox` en OPTIONNEL : sans
        // cette ligne le service consomme sans garde, avec un simple avertissement.
        // Le seul gestionnaire d'aujourd'hui survit à un rejeu grâce à sa garde
        // d'état — mais c'est une propriété de SON code, pas du service, et le
        // prochain naîtrait sans elle.

        // ═════════════════════════════════════════════════════════════════════
        // CETTE LIGNE ENREGISTRAIT UNE TARIFICATION NEUTRE, ET C'ÉTAIT
        // ISSUE-033 (CRITICAL) À ELLE SEULE.
        //
        // Ce bouchon était la SEULE implémentation d'`IPricingModuleApi` du dépôt,
        // enregistrée sans garde d'environnement — production comprise. Il rendait
        // `SellerDiscount: 0, PlatformDiscount: 0, FinalAmount = BaseAmount` et
        // refusait TOUT coupon. Autrement dit : aucune campagne commerciale n'était
        // possible sur la place de marché, et promotion-service — complet, avec son
        // domaine, ses règles, ses coupons, ses budgets et son API gRPC — n'était
        // appelé par personne.
        //
        // Rien ne le signalait. Un panier valorisé sans remise ressemble
        // exactement à un panier sans coupon, et un code refusé ressemble à un code
        // périmé. Le seul symptôme était commercial : « pourquoi nos campagnes ne
        // marchent-elles jamais ? ».
        //
        // L'IMPLÉMENTATION EST DÉSORMAIS RÉELLE, ET LE BOUCHON A ÉTÉ RETIRÉ.
        //
        // Il n'est pas gardé, il n'existe plus dans ce service : le fichier est
        // parti dans `_to_delete/`. Le garder « au cas où » aurait laissé une ligne
        // d'enregistrement à une substitution près du silence d'origine.
        //
        // Le repli en cas de panne vit maintenant DANS l'adaptateur réel, où il est
        // JOURNALISÉ : promotion-service injoignable rend le prix de base et laisse
        // la vente se faire, en le disant. Voir `PromotionPricingModuleApi`.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IPricingModuleApi, PromotionPricingModuleApi>();

        services.AddScoped<IDomainEventHandler<CartCheckedOutDomainEvent>, CartCheckedOutDomainEventHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
