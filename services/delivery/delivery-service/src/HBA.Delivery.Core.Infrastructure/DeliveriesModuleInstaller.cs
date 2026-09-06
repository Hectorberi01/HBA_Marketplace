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
        // LE CACHE DE CE SERVICE (Caching/Redis/). Il etait branche par le
        // socle pour les vingt-six services a la fois ; il l'est desormais ici.
        services.AjouterCacheDeliveryCore(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc
        // hors de cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieDeliveryCore()`, que le composition root peut oublier.
        // Un oubli ne casserait rien de visible — le service demarre et n'emet
        // plus rien. Cette garde, elle, est enregistree ici : elle doit exister
        // quand ce qu'elle verifie est absent.
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
        //
        // Ils etaient enregistres ici, un par un, chacun precede de la raison
        // qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
        // `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
        // produit deux mensonges : un commentaire sans code, du code sans raison.
        services.AjouterClientsGrpcDeliveryCore(configuration);
        services.AddScoped<IDeliveryPricingQuoteValidator, GrpcDeliveryPricingQuoteValidator>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();

        // ─────────────────────────────────────────────────────────────────────
        // LA GARDE D'IDEMPOTENCE DE CONSOMMATION (§19.5).
        //
        // Sans cet enregistrement, `IntegrationEventDispatcher` ne trouve aucune
        // inbox, se contente d'un avertissement au journal, et les six
        // enregistreurs de webhook partenaire tournent NUS. Kafka livre au moins
        // une fois : au premier rééquilibrage de partitions, le partenaire reçoit
        // deux fois « delivery.completed » pour la même course — et facture deux
        // fois la livraison qu'il n'a faite qu'une.
        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // LE TAUX DE PARTAGE EST VALIDÉ AU DÉMARRAGE, PAS À LA PREMIÈRE REMISE.
        //
        // Construire l'objet ici plutôt que de le résoudre paresseusement fait
        // échouer le démarrage sur un réglage aberrant — « 700 » au lieu de
        // « 70 » — au lieu de le découvrir sur le premier décompte de livreur.
        // Singleton : la configuration ne change pas en cours d'exécution.
        // ─────────────────────────────────────────────────────────────────────
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

        // Traduction des faits internes en faits publics. Seuls les événements
        // ACTIONNABLES par un tiers sont enregistrés : ceux du dispatch —
        // proposition, refus, relance — restent dans le module.
        services.AddScoped<IDomainEventHandler<DeliveryCreatedDomainEvent>, DeliveryCreatedDomainEventHandler>();

        // CET ENREGISTREMENT MANQUAIT, ET LE MODULE ENTIER EN DÉPENDAIT.
        //
        // DeliveryAssignedDomainEvent était levé par l'agrégat et écouté par
        // personne. Le dispatch proposait, attendait quarante-cinq secondes,
        // expirait, recommençait cinq fois, puis déclarait « aucun livreur
        // disponible » — pendant que des livreurs en ligne regardaient un écran
        // vide. Aucun test ne pouvait le voir : le domaine était correct, le
        // dispatch était correct, et rien ne reliait les deux au monde extérieur.
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


        // La boucle de dispatch : sans elle, une course est créée, passe en
        // « recherche de livreur » et y reste indéfiniment. Un seul processus la
        // fait tourner — voir DispatchToggle.
        if (DispatchToggle.Enabled)
        {
            services.AddHostedService<DeliveryDispatchService>();
        }
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES WEBHOOKS PARTENAIRES.
    ///
    /// Le client HTTP est NOMMÉ et sa durée de vie gérée par la fabrique : un
    /// HttpClient construit à la main par appel épuise les sockets, un HttpClient
    /// statique ne voit jamais un changement de DNS. La fabrique règle les deux.
    ///
    /// PAS DE REDIRECTION AUTOMATIQUE.
    ///
    /// Une redirection ferait repartir la requête vers une adresse que nous
    /// n'avons pas validée — et .NET ne rejoue pas le corps ni les en-têtes
    /// personnalisés sur une redirection, donc la signature disparaîtrait en
    /// chemin. Le partenaire recevrait un appel non signé et le refuserait, sans
    /// que personne ne comprenne pourquoi.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static void RegisterWebhooks(IServiceCollection services)
    {

        services.AddHttpClient(WebhookDispatchService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        // Même interrupteur que le dispatch : un seul processus doit vider la
        // file, sinon deux instances enverraient le même webhook en double.
        if (DispatchToggle.Enabled)
        {
            services.AddHostedService<WebhookDispatchService>();
        }
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SANS REDIS, LE DISPATCH NE FONCTIONNE PAS — SAUF EN DÉVELOPPEMENT.
    ///
    /// Les autres modules se contentent d'un cache mémoire quand Redis est
    /// absent : ils y perdent en performance, pas en exactitude. Ici, non. Les
    /// positions des livreurs SONT le cache ; réparties sur deux instances qui
    /// ne partagent rien, chacune ne verrait que « sa » flotte, et le dispatch
    /// manquerait la moitié des livreurs sans que rien ne le signale.
    ///
    /// Hors développement, l'absence de Redis est donc une ERREUR DE DÉMARRAGE.
    ///
    /// En développement, elle ne peut pas l'être : refuser de démarrer y
    /// obligerait quiconque travaille sur le catalogue ou les commandes à faire
    /// tourner un Redis pour un module qui ne le concerne pas. Le repli mémoire
    /// s'applique alors — et il s'annonce, bruyamment, à chaque démarrage. Ce
    /// n'est pas la même chose qu'un repli silencieux : c'est ce dernier qui
    /// laisse découvrir la panne en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static void RegisterLocationCache(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            // LES DEUX NOMS, PAS UN SEUL.
            //
            // « ASPNETCORE_ENVIRONMENT » est celui d'un hôte web ; un hôte
            // générique — service de fond, test de composition, futur worker —
            // utilise « DOTNET_ENVIRONMENT ». Ne lire que le premier faisait
            // passer tout hôte non-web pour de la production, et cette garde
            // refusait alors de démarrer un test qui n'a rien à voir avec Redis.
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
        // connexions et sa reconnexion. En créer un par requête ouvrirait une
        // connexion TCP par appel — la première cause d'épuisement de sockets sur
        // les applications qui utilisent StackExchange.Redis.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnection));

        services.AddScoped<IDriverLocationCache, RedisDriverLocationCache>();
    }
}
