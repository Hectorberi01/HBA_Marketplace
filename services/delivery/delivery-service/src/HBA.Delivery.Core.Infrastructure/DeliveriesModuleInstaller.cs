using System.Reflection;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Deliveries.EventHandlers;
using HBA.Deliveries.Application.Drivers;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Deliveries.Contracts;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Deliveries.Events;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Drivers.Events;
using HBA.Deliveries.Domain.Partners;
using HBA.Deliveries.Domain.Webhooks;
using HBA.Deliveries.Infrastructure.Caching;
using HBA.Deliveries.Infrastructure.Configuration;
using HBA.Deliveries.Infrastructure.Dispatch;
using HBA.Deliveries.Infrastructure.Persistence;
using HBA.Deliveries.Infrastructure.Pricing;
using HBA.Deliveries.Infrastructure.Public;
using HBA.Deliveries.Infrastructure.Webhooks;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

using HBA.Deliveries.Infrastructure.Grpc;
using HBA.Deliveries.Infrastructure.Caching.Redis;
using HBA.Deliveries.Infrastructure.Observability;
namespace HBA.Deliveries.Infrastructure;

/// <summary>
/// Enregistre le module Deliveries : DbContext, dépôts, cache de positions,
/// validateurs et outbox.
/// </summary>
public sealed class DeliveriesModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Deliveries";

    public Assembly ApplicationAssembly => typeof(CreateDeliveryCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheDeliveryCore(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteDeliveryCore(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieDeliveryCore()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<DeliveriesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveriesDbContext.SchemaName)));

        services.AddScoped<IDeliveryUnitOfWork>(sp => sp.GetRequiredService<DeliveriesDbContext>());

        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<IDeliveryModuleApi, DeliveryModuleApi>();

        services.AddScoped<IPartnerRepository, PartnerRepository>();
        // LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
        services.AjouterClientsGrpcDeliveryCore(configuration);
        services.AddScoped<IDeliveryPricingQuoteValidator, GrpcDeliveryPricingQuoteValidator>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();

        // LA GARDE D'IDEMPOTENCE DE CONSOMMATION (§19.5).

        // LE TAUX DE PARTAGE EST VALIDÉ AU DÉMARRAGE, PAS À LA PREMIÈRE REMISE.
        var payout = new DeliveryPayoutSettings(configuration);
        services.AddSingleton<IDeliveryPayoutSettings>(payout);

        if (payout.UsesDefault)
        {
            // Volontairement sur la sortie standard : le conteneur d'injection
            // n'est pas encore construit, donc aucun journal structuré n'existe.
            Console.WriteLine(
                $"[Deliveries] « {DeliveryPayoutSettings.SectionKey} » absent — part du livreur fixée à "
                + $"{DeliveryPayoutSettings.DefaultSharePercent} % PAR DÉFAUT. Cette valeur n'a pas été "
                + "validée commercialement.");
        }

        RegisterLocationCache(services, configuration);

        // Traduction des faits internes en faits publics.
        services.AddScoped<IDomainEventHandler<DeliveryCreatedDomainEvent>, DeliveryCreatedDomainEventHandler>();

        // CET ENREGISTREMENT MANQUAIT, ET LE MODULE ENTIER EN DÉPENDAIT.
        services.AddScoped<IDomainEventHandler<DeliveryAssignedDomainEvent>, DeliveryAssignedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DriverVerifiedDomainEvent>, DriverVerifiedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DeliveryAcceptedDomainEvent>, DeliveryAcceptedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DeliveryPickedUpDomainEvent>, DeliveryPickedUpDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DeliveryCompletedDomainEvent>, DeliveryCompletedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DeliveryCancelledDomainEvent>, DeliveryCancelledDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DeliveryNoDriverAvailableDomainEvent>, DeliveryNoDriverAvailableDomainEventHandler>();

        RegisterWebhooks(services);

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);


        // La boucle de dispatch : sans elle, une course est créée, passe en «
        // recherche de livreur » et y reste indéfiniment.
        if (DispatchToggle.Enabled)
        {
            services.AddHostedService<DeliveryDispatchService>();
        }
    }

    /// <summary>LES WEBHOOKS PARTENAIRES.</summary>
    private static void RegisterWebhooks(IServiceCollection services)
    {

        services.AddHttpClient(WebhookDispatchService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        // Même interrupteur que le dispatch : un seul processus doit vider la file,
        // sinon deux instances enverraient le même webhook en double.
        if (DispatchToggle.Enabled)
        {
            services.AddHostedService<WebhookDispatchService>();
        }
    }

    /// <summary>SANS REDIS, LE DISPATCH NE FONCTIONNE PAS — SAUF EN DÉVELOPPEMENT.</summary>
    private static void RegisterLocationCache(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            // LES DEUX NOMS, PAS UN SEUL.
            var environment =
                configuration["ASPNETCORE_ENVIRONMENT"]
                ?? configuration["DOTNET_ENVIRONMENT"]
                ?? "Production";
            var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);

            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    $"Le module Deliveries exige Redis en environnement « {environment} » : renseignez "
                    + "« Redis:ConnectionString ». Les positions des livreurs y vivent, et deux instances "
                    + "sans cache partagé ne verraient chacune qu'une partie de la flotte.");
            }

            // Volontairement écrit sur la sortie standard : à cet instant, le
            // conteneur d'injection n'est pas construit, donc aucun journal
            // structuré n'est disponible.
            Console.WriteLine(
                "[Deliveries] Redis absent — positions des livreurs EN MÉMOIRE (développement uniquement). "
                + "Le dispatch ne fonctionnera pas au-delà d'un seul processus.");

            services.AddSingleton<IDriverLocationCache, InMemoryDriverLocationCache>();
            return;
        }

        // Le multiplexeur est un SINGLETON : il gère lui-même son pool de
        // connexions et sa reconnexion.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnection));

        services.AddScoped<IDriverLocationCache, RedisDriverLocationCache>();
    }
}
