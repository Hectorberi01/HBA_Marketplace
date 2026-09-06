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

        // LA PARESSE DECRITE JUSTE AU-DESSUS EST DESORMAIS CORRIGEE ICI.
        //
        // Ce service hebergé resout `ISecretProtector` au demarrage, donc force la
        // fabrique a s'executer avant que l'hote ne serve. Une cle absente, mal
        // encodee ou de mauvaise taille arrete le conteneur au lieu de rendre un
        // 500 muet a la premiere inscription. Voir
        // `VerificationDesSecretsAuDemarrage` — y compris ce qu'il ne couvre pas.
        services.AddHostedService<VerificationDesSecretsAuDemarrage>();
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
                Enabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase),

                // LA LIGNE QUI MANQUAIT. Sans elle, `SubscribeTopics` restait vide
                // quoi qu'on écrive : la propriété était documentée, honorée par le
                // consommateur, et remplie par personne. Voir `AbonnementsKafka`.
                //
                // Absent = tous les sujets, comme avant. La migration se fait donc
                // service par service.
                // L'UNION, ET NON LE DERNIER INSCRIT.
                //
                // `GetService` ne rend que le DERNIER enregistrement. Trois hotes
                // composent plusieurs modules dans un seul processus —
                // `HBA.Financial.Api` (payments, wallet, billing),
                // `HBA.Engagement.Api` (reviews, recommendations, wishlist),
                // `HBA.Communication.Api` — et chacun de leurs modules declare ses
                // propres sujets. Avec `GetService`, deux modules sur trois
                // n'auraient JAMAIS ete abonnes : leurs gestionnaires seraient
                // enregistres, corrects, et jamais appeles. Aucune erreur.
                SubscribeTopics = sp.GetServices<AbonnementsKafka>()
                    .SelectMany(abonnements => abonnements.Sujets)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(sujet => sujet, StringComparer.Ordinal)
                    .ToArray(),

                // Vrai des qu'un module a declare ses abonnements, MEME VIDES.
                // Voir `KafkaEventBusOptions.AbonnementsDeclares`.
                AbonnementsDeclares = sp.GetServices<AbonnementsKafka>().Any()
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
        // LE CACHE A QUITTÉ CE FICHIER — IL APPARTIENT AUX SERVICES.
        //
        // `DistributedCacheService`, `NoOpCacheService` et le choix Redis /
        // mémoire étaient posés ici, donc pour les vingt-six services à la fois.
        // Chaque service porte désormais le sien dans
        // `Infrastructure/Caching/Redis/`, et le branche par
        // `AjouterCache<Service>()` depuis son installeur.
        //
        // CE QUI RESTE PARTAGÉ : `ICacheService`, dans
        // `HBA.Shared.Application.Abstractions`. C'est le PORT dont dépend la
        // couche Application de cinq services — pas l'adaptateur.
        //
        // CE QUE LE DÉPLACEMENT COÛTE, ET IL FAUT LE SAVOIR : un service qui
        // n'appelle pas `AjouterCache<Service>()` n'a plus AUCUN `ICacheService`.
        // La résolution échoue au démarrage — bruyamment, donc — mais elle
        // échoue, là où ce fichier garantissait la présence. C'est le prix de
        // l'autonomie, et il se paie une fois par service oublié.
        // ═════════════════════════════════════════════════════════════════════

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
