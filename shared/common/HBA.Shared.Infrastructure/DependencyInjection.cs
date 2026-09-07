using HBA.Shared.Infrastructure.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Observability;
using HBA.Shared.Infrastructure.Events;
using HBA.Shared.Infrastructure.Kafka;
using HBA.Shared.Infrastructure.Observability;
using HBA.Shared.IntegrationEvents;
using HBA.Shared.Infrastructure.Security;

namespace HBA.Shared.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Services transverses d'infrastructure partagés par tous les modules :
    /// dispatch des domain events, dispatch in-process des integration events, et
    /// la file scopée d'events d'intégration (drainée par le DbContext du module
    /// vers son outbox).
    /// </summary>
    /// <param name="configuration">
    /// Nécessaire au choix du cache distribué : la décision « Redis ou mémoire » se
    /// prend à l'enregistrement, pas à la résolution.
    /// </param>
    public static IServiceCollection AddBuildingBlocksInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IntegrationEventDispatcher>();

        // CHIFFREMENT DES SECRETS QUI TRAVERSENT LE BUS.
        services.AddSingleton<ISecretProtector>(_ =>
            AesGcmSecretProtector.Depuis(configuration, EstProduction(configuration)));

        // LA PARESSE DECRITE JUSTE AU-DESSUS EST DESORMAIS CORRIGEE ICI.
        services.AddHostedService<VerificationDesSecretsAuDemarrage>();
        services.AddSingleton<IOptions<KafkaEventBusOptions>>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var enabled = configuration["Kafka:Enabled"];

            // LES DÉFAUTS VIENNENT DE LA CLASSE, PAS DE LITTÉRAUX RECOPIÉS.
            var defauts = new KafkaEventBusOptions();

            var options = new KafkaEventBusOptions
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                TopicPrefix = configuration["Kafka:TopicPrefix"] ?? defauts.TopicPrefix,
                TopicVersion = configuration["Kafka:TopicVersion"] ?? defauts.TopicVersion,
                Producer = configuration["Kafka:Producer"],
                ProducerVersion = configuration["Kafka:ProducerVersion"],
                ConsumerGroup = configuration["Kafka:ConsumerGroup"],
                Enabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase),

                // LA LIGNE QUI MANQUAIT. Sans elle, `SubscribeTopics` restait vide
                // quoi qu'on écrive : la propriété était documentée, honorée par le
                // consommateur, et remplie par personne.
                SubscribeTopics = sp.GetServices<AbonnementsKafka>()
                    .SelectMany(abonnements => abonnements.Sujets)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(sujet => sujet, StringComparer.Ordinal)
                    .ToArray(),

                // Vrai des qu'un module a declare ses abonnements, MEME VIDES. Voir
                // `KafkaEventBusOptions.AbonnementsDeclares`.
                AbonnementsDeclares = sp.GetServices<AbonnementsKafka>().Any()
            };

            // ON REFUSE DE DÉMARRER SI PUBLICATION ET ABONNEMENT DIVERGENT.
            var attendu = $"{options.TopicPrefix}.";
            var ecoutes = options.SubscribeTopics is { Length: > 0 }
                ? options.SubscribeTopics
                : HbaTopics.Tous(options).ToArray();

            var orphelins = ecoutes
                .Where(topic => !topic.StartsWith(attendu, StringComparison.Ordinal))
                .ToArray();

            if (orphelins.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Kafka : le préfixe de publication est « {options.TopicPrefix} » mais "
                    + $"{orphelins.Length} sujet(s) écouté(s) n'en dépendent pas — par exemple "
                    + $"« {orphelins[0]} ». Les événements publiés n'auraient aucun consommateur, "
                    + "et rien ne le signalerait à l'exécution.");
            }

            // UNE OUTBOX QUI DRAINE VERS UN PRODUCTEUR ABSENT DÉTRUIT LES
            // ÉVÉNEMENTS. ON REFUSE DE DÉMARRER DANS CETTE CONFIGURATION.
            var producteurIndisponible = !options.Enabled
                                         || string.IsNullOrWhiteSpace(options.BootstrapServers);

            if (producteurIndisponible && DrainageDOutbox.Actif)
            {
                throw new InvalidOperationException(
                    "Kafka : le producteur est indisponible ("
                    + (options.Enabled ? "Kafka:BootstrapServers absent" : "Kafka:Enabled=false")
                    + ") alors que le processeur d'outbox draine (OUTBOX_ENABLED n'est pas « false »). "
                    + "Chaque événement d'intégration serait retiré de l'outbox sans jamais être "
                    + "publié — attribution des rôles, création des profils, ponts inter-services. "
                    + "Renseigner Kafka:BootstrapServers, ou poser OUTBOX_ENABLED=false sur cet hôte.");
            }

            return Options.Create(options);
        });
        services.AddSingleton<IKafkaIntegrationEventPublisher, KafkaIntegrationEventPublisher>();
        services.AddHostedService<KafkaIntegrationEventConsumer>();

        // Une file scopée par requête ; le même objet sert de publisher (côté
        // handlers) et de source de drainage (côté DbContext du module).
        services.AddScoped<IntegrationEventQueue>();
        services.AddScoped<IIntegrationEventPublisher>(sp => sp.GetRequiredService<IntegrationEventQueue>());

        // LES METRIQUES NEUTRES ONT QUITTE CE FICHIER.

        // LE CACHE A QUITTÉ CE FICHIER — IL APPARTIENT AUX SERVICES.

        return services;
    }

    /// <summary>Sommes-nous en production ?</summary>
    private static bool EstProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        return EnvironnementDeploiement.EstProduction(configuration);
    }
}
