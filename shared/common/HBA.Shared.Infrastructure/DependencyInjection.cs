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
using HBA.Shared.Infrastructure.Caching;
using HBA.Shared.Infrastructure.Events;
using HBA.Shared.Infrastructure.Kafka;
using HBA.Shared.Infrastructure.Observability;
using HBA.Shared.IntegrationEvents;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Security;

namespace HBA.Shared.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Services transverses d'infrastructure partagés par tous les modules :
    /// dispatch des domain events, dispatch in-process des integration events,
    /// et la file scopée d'events d'intégration (drainée par le DbContext du
    /// module vers son outbox). À appeler une fois dans le Bootstrap.
    /// </summary>
    /// <param name="configuration">
    /// Nécessaire au choix du cache distribué : la décision « Redis ou mémoire »
    /// se prend à l'enregistrement, pas à la résolution.
    /// </param>
    public static IServiceCollection AddBuildingBlocksInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IntegrationEventDispatcher>();

        // ═════════════════════════════════════════════════════════════════════
        // CHIFFREMENT DES SECRETS QUI TRAVERSENT LE BUS.
        //
        // Enregistré POUR TOUS les services, et pas seulement pour identity et
        // notifications : le producteur et le consommateur d'un secret ne sont pas
        // toujours ceux qu'on croit, et l'invitation vendeur a exactement le même
        // défaut côté seller-service. Un service qui ne s'en sert pas ne paie
        // qu'un objet en mémoire.
        //
        // Singleton : la clé est lue une fois, et `AesGcm` est instancié par appel.
        // En faire un scoped relirait la configuration à chaque requête.
        //
        // IL LÈVE EN PRODUCTION SI LA CLÉ MANQUE — MAIS PAS AU DÉMARRAGE, ET LA
        // NUANCE COMPTE.
        //
        // Ce paragraphe annonçait « au démarrage ». C'est faux : la fabrique est
        // PARESSEUSE. Un singleton enregistré par lambda n'est construit qu'à la
        // PREMIÈRE RÉSOLUTION d'`ISecretProtector`, c'est-à-dire à la première
        // demande de réinitialisation de mot de passe ou de vérification
        // d'adresse — pas au boot.
        //
        // Conséquence à connaître : un service déployé en production sans
        // `Security:SecretProtection:Key` démarre NORMALEMENT, passe ses sondes,
        // sert son trafic, et n'échoue que le jour où un utilisateur demande un
        // code. Le refus est correct ; ce qui était trompeur, c'est de croire
        // qu'un déploiement réussi valait vérification de la clé.
        //
        // Voir `AesGcmSecretProtector.Depuis` — un secret qu'on croit chiffré et
        // qui ne l'est pas est pire que pas de chiffrement du tout, parce que
        // personne ne le vérifie deux fois.
        // ═════════════════════════════════════════════════════════════════════
        services.AddSingleton<ISecretProtector>(_ =>
            AesGcmSecretProtector.Depuis(configuration, EstProduction(configuration)));
        services.AddSingleton<IOptions<KafkaEventBusOptions>>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var enabled = configuration["Kafka:Enabled"];

            // ═════════════════════════════════════════════════════════════════
            // LES DÉFAUTS VIENNENT DE LA CLASSE, PAS DE LITTÉRAUX RECOPIÉS.
            //
            // Cette ligne portait `?? "livraison"` — un vestige de l'époque où ce
            // socle servait HBA Delivery. Or `KafkaEventBusOptions.TopicPrefix`
            // déclare « service », et `SubscribeTopics` liste treize sujets
            // « service.*.v1 ».
            //
            // Résultat : les PRODUCTEURS écrivaient dans `livraison.identity.v1`
            // pendant que les CONSOMMATEURS s'abonnaient à `service.identity.v1`.
            // Toute la couche événementielle était morte — attribution des rôles,
            // création des profils, ponts inter-services — sans une seule erreur.
            // Le courtier créait docilement les sujets du producteur, et les
            // consommateurs attendaient sur des sujets vides.
            //
            // Deux valeurs par défaut pour une même notion, à deux endroits, sont
            // condamnées à diverger. On lit désormais celle de la classe.
            // ═════════════════════════════════════════════════════════════════
            var defauts = new KafkaEventBusOptions();

            var options = new KafkaEventBusOptions
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                TopicPrefix = configuration["Kafka:TopicPrefix"] ?? defauts.TopicPrefix,
                TopicVersion = configuration["Kafka:TopicVersion"] ?? defauts.TopicVersion,
                Producer = configuration["Kafka:Producer"],
                ProducerVersion = configuration["Kafka:ProducerVersion"],
                ConsumerGroup = configuration["Kafka:ConsumerGroup"],
                Enabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase)
            };

            // ON REFUSE DE DÉMARRER SI PUBLICATION ET ABONNEMENT DIVERGENT.
            //
            // C'est la seule protection possible contre le retour du même défaut :
            // un préfixe configuré à la main qui ne correspondrait plus aux sujets
            // écoutés. Échouer au démarrage est brutal, et infiniment préférable à
            // une plateforme qui tourne en publiant dans le vide.
            //
            // ON CONTRÔLE LA LISTE RÉELLEMENT UTILISÉE, PAS LA PROPRIÉTÉ.
            //
            // `SubscribeTopics` est vide par défaut depuis la correction d'ISSUE-001 :
            // c'est `HbaTopics.Tous` qui fournit les sujets, et ceux-là dérivent du
            // préfixe par construction. Contrôler la propriété vide ferait passer ce
            // test sans rien vérifier — un contrôle qui se tait à tort est pire que
            // pas de contrôle. On éprouve donc ce à quoi le consommateur s'abonnera.
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

            // ═════════════════════════════════════════════════════════════════
            // UNE OUTBOX QUI DRAINE VERS UN PRODUCTEUR ABSENT DÉTRUIT LES
            //    ÉVÉNEMENTS. ON REFUSE DE DÉMARRER DANS CETTE CONFIGURATION.
            //
            // Le processeur d'outbox appelle `PublishAsync` puis marque la ligne
            // traitée. Quand le producteur n'existe pas — `Kafka:Enabled=false`
            // ou `Kafka:BootstrapServers` vide — l'ancien publieur rendait la
            // main EN SUCCÈS : la ligne d'outbox était consommée, rien ne
            // partait, et la seule trace était un `LogDebug`. L'événement
            // n'était pas retardé, il était SUPPRIMÉ, sans rejeu possible.
            //
            // C'est exactement ce qui fait qu'un vendeur s'inscrit, que
            // « vendeur inscrit » disparaît, et que son compte reste `Buyer`.
            //
            // Les deux réglages doivent donc varier ENSEMBLE : un hôte qui ne
            // publie pas ne doit pas drainer. Les harnais de test posent bien
            // les deux (`OUTBOX_ENABLED=false` ET `Kafka__Enabled=false`).
            // ═════════════════════════════════════════════════════════════════
            var producteurIndisponible = !options.Enabled
                                         || string.IsNullOrWhiteSpace(options.BootstrapServers);

            if (producteurIndisponible && OutboxRegistration.Enabled)
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

        services.TryAddSingleton<IPaymentMetrics, NoOpPaymentMetrics>();
        services.TryAddSingleton<IHbaBusinessMetrics, NoOpBusinessMetrics>();
        services.TryAddSingleton<ISecurityMetrics, NoOpSecurityMetrics>();
        services.TryAddSingleton<IOutboxMetrics, NoOpOutboxMetrics>();

        // ═════════════════════════════════════════════════════════════════════
        // LE CACHE DISTRIBUÉ — ET IL NE L'ÉTAIT PAS.
        //
        // CETTE LIGNE ÉTAIT `AddDistributedMemoryCache()`, SOUS UN COMMENTAIRE
        //    QUI AFFIRMAIT LE CONTRAIRE.
        //
        // Il annonçait que « le Bootstrap remplace l'IDistributedCache par Redis
        // quand Redis:ConnectionString est renseigné ». Ce remplacement n'existait
        // nulle part : `AddStackExchangeRedisCache` n'apparaissait dans aucun
        // fichier du dépôt, et le paquet n'était référencé que par
        // delivery-service, qui a son propre client. Toute la plateforme cachait
        // donc EN MÉMOIRE, PAR INSTANCE, y compris en production.
        //
        // CE QUE CELA COÛTAIT : L'INVALIDATION NE TOUCHAIT QU'UNE RÉPLIQUE.
        //
        // Un cache par instance n'est pas seulement « moins efficace » : il est
        // FAUX dès qu'on l'invalide. Une éviction déclenchée par un `SaveChanges`
        // ne vide que le dictionnaire du processus qui a écrit ; les N−1 autres
        // continuent de servir la valeur périmée jusqu'au TTL. Sur un événement
        // Kafka, c'est pire encore — dans un groupe de consommateurs, une SEULE
        // instance reçoit le message.
        //
        // C'est ce qui rendait invérifiable la promesse de coupure immédiate d'un
        // accès suspendu : le membre révoqué continuait d'être autorisé par toutes
        // les instances sauf une.
        //
        // ABSENT, ON RETOMBE EN MÉMOIRE — MAIS BRUYAMMENT.
        //
        // Le repli reste possible : un poste de développement n'a pas toujours un
        // Redis. Ce qu'on ne refait pas, c'est le repli SILENCIEUX, celui qui se
        // découvre en production. Le message part sur la sortie standard parce
        // qu'à cet instant le conteneur d'injection n'est pas construit — même
        // technique que `DeliveriesModuleInstaller`.
        // ═════════════════════════════════════════════════════════════════════
        var redis = configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(redis))
        {
            Console.WriteLine(
                "[HBA] Redis absent — cache EN MÉMOIRE, par instance. "
                + "Toute invalidation ne touchera que le processus qui l'a déclenchée : "
                + "les autres répliques serviront des valeurs périmées jusqu'au TTL. "
                + "Renseignez « Redis:ConnectionString » hors développement.");

            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;

                // UN PRÉFIXE COMMUN, ET NON UN PAR SERVICE.
                //
                // Les treize services partagent une instance Redis. Le préfixe les
                // isole d'un autre locataire éventuel, sans les isoler les uns des
                // autres — ce qui serait un contresens : les clés sont déjà
                // nommées par domaine (`sellers:`, `catalog:`, `cart:`), et deux
                // répliques du MÊME service doivent impérativement partager la
                // leur, sans quoi on retrouverait exactement le défaut qu'on
                // corrige ici.
                options.InstanceName = "hba:";
            });
        }

        // Le logger est résolu en OPTIONNEL, et c'est délibéré.
        //
        // DistributedCacheService journalise les pannes de cache (Redis injoignable,
        // invalidation ratée). Mais exiger un ILogger ferait échouer tout conteneur
        // monté sans AddLogging() — c'est le cas de plusieurs harnais de tests, qui
        // n'installent qu'un module. Le cache aurait alors cassé des tests qui
        // n'ont rien à voir avec lui.
        //
        // Un service transverse ne doit pas imposer ses dépendances de confort à
        // ceux qui l'utilisent. Sans logger : NullLogger, et tout fonctionne.
        services.AddSingleton<ICacheService>(sp => new DistributedCacheService(
            sp.GetRequiredService<IDistributedCache>(),
            sp.GetService<ILogger<DistributedCacheService>>() ?? NullLogger<DistributedCacheService>.Instance));

        return services;
    }

    /// <summary>
    /// Sommes-nous en production ?
    /// </summary>
    /// <remarks>
    /// L'installeur ne reçoit qu'un <see cref="IConfiguration"/> — les modules
    /// s'installent avant que l'hôte ne soit construit, donc pas
    /// d'<c>IHostEnvironment</c>. La règle elle-même vit dans
    /// <c>EnvironnementDeploiement</c>, en un seul exemplaire.
    ///
    /// CE PARAGRAPHE DÉCRIVAIT AUPARAVANT UN FAIL-OPEN ASSUMÉ : « l'inconnu est
    /// traité comme pas la production, sinon un nom mal orthographié empêcherait
    /// de travailler ». Ce n'est plus vrai, et ce n'était pas défendable : une
    /// variable ABSENTE tombait du même côté qu'une faute de frappe, alors
    /// qu'ASP.NET Core considère une variable absente comme la production.
    /// Désormais l'inconnu et l'absent sont la production ; seuls les noms
    /// explicitement listés en dispensent.
    /// </remarks>
    private static bool EstProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        //
        // Ce corps était une copie parmi six d'une règle FAIL-OPEN : tout ce qui
        // n'était pas littéralement « Production » — variable absente, chaîne
        // vide, faute de frappe — était traité comme du développement, alors
        // qu'ASP.NET Core, lui, considère une variable absente comme la
        // production. Voir l'encadré de `EnvironnementDeploiement`.
        return EnvironnementDeploiement.EstProduction(configuration);
    }
}
