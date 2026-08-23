using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HBA.Shared.Infrastructure.Kafka;

public interface IKafkaIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

public sealed class KafkaIntegrationEventPublisher : IKafkaIntegrationEventPublisher, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IProducer<string, string>? _producer;
    private readonly KafkaEventBusOptions _options;

    /// <summary>Producteurs hors catalogue déjà signalés — un avertissement, pas un par message.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _catalogueIncomplet = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaIntegrationEventPublisher> _logger;

    public KafkaIntegrationEventPublisher(
        IOptions<KafkaEventBusOptions> options,
        IConfiguration configuration,
        ILogger<KafkaIntegrationEventPublisher> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;

        if (_options.Enabled && !string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true
            }).Build();
        }
        else
        {
            // Critical dès la construction : ce service ne publiera RIEN de toute
            // sa vie. Le découvrir au démarrage vaut mieux que le déduire d'une
            // table de rôles vide trois jours plus tard.
            _logger.LogCritical(
                "Producteur Kafka NON CONSTRUIT ({Cause}). Ce service ne publiera aucun "
                + "événement d'intégration tant qu'il vivra.",
                _options.Enabled ? "Kafka:BootstrapServers absent" : "Kafka:Enabled=false");
        }
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ON LÈVE QUAND LE PRODUCTEUR MANQUE. ON NE REND PLUS LA MAIN EN SUCCÈS.
    ///
    /// Cette méthode faisait ceci quand `_producer` était nul :
    ///
    ///     _logger.LogDebug("Kafka désactivé …, event non publié.");
    ///     return;                                  // ← retour EN SUCCÈS
    ///
    /// Son SEUL appelant est `OutboxProcessor`, qui enchaîne aussitôt :
    ///
    ///     await publisher.PublishAsync(…);
    ///     message.ProcessedOnUtc = DateTime.UtcNow;   // la ligne est consommée
    ///
    /// L'événement n'était donc pas retardé : il était SUPPRIMÉ. Ligne d'outbox
    /// marquée traitée, `AttemptCount` à zéro, aucune lettre morte, aucune
    /// métrique, et pour toute trace un `LogDebug` — invisible au niveau
    /// `Information` par défaut. Rien à rejouer, rien à voir.
    ///
    /// C'est ce maillon qui explique la panne à l'origine de ce correctif : un
    /// vendeur s'inscrit, `SellerRegisteredIntegrationEvent` part à l'outbox,
    /// l'outbox le « traite », identity-service ne reçoit jamais rien, et le
    /// compte reste `Buyer`. L'application vendeur rend 403 sans qu'une seule
    /// ligne de journal relie le refus à l'inscription.
    ///
    /// En levant, on rend la main à la politique de reprise de l'outbox : trois
    /// warnings, un backoff, puis une LETTRE MORTE journalisée en Critical avec
    /// sa métrique — et le message reste rejouable par
    /// `/admin/outbox/dead-letters` une fois la configuration corrigée.
    ///
    /// Cause STRUCTURELLE, pas passagère : lever la fait tourner en boucle
    /// jusqu'au plafond. C'est assumé, et c'est pourquoi
    /// `AddBuildingBlocksInfrastructure` refuse désormais de démarrer un hôte
    /// qui draine l'outbox sans producteur. Ce garde-fou-ci est le second filet.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (_producer is null)
        {
            throw new InvalidOperationException(
                $"Événement « {integrationEvent.GetType().Name} » NON PUBLIABLE : le producteur "
                + "Kafka n'existe pas ("
                + (_options.Enabled ? "Kafka:BootstrapServers absent" : "Kafka:Enabled=false")
                + "). Le message reste dans l'outbox et finira en lettre morte. "
                + "Renseigner Kafka:BootstrapServers, puis rejouer via /admin/outbox/dead-letters.");
        }

        var producer = KafkaEventNaming.Producer(_options.Producer, _configuration["SERVICE_NAME"]);
        var eventType = KafkaEventNaming.EventType(integrationEvent.GetType());
        var aggregateId = KafkaEventNaming.AggregateId(integrationEvent);
        var eventId = KafkaEventNaming.UlidFrom(integrationEvent.Id, integrationEvent.OccurredOnUtc);
        var publishedAt = DateTimeOffset.UtcNow;
        // ═════════════════════════════════════════════════════════════════════
        // LE SUJET VIENT DU CATALOGUE, PLUS D'UNE DÉRIVATION DU NOM DU CONTENEUR.
        //
        // `KafkaEventNaming.Topic` retirait « -service » du `SERVICE_NAME` :
        // seller-service publiait donc sur `service.seller.v1`, quand tous les
        // consommateurs écoutaient `service.merchant.v1`. Six domaines étaient dans
        // ce cas (ISSUE-001). `HbaTopics` porte la traduction, et c'est la MÊME
        // table qui alimente la liste d'abonnement du consommateur.
        // ═════════════════════════════════════════════════════════════════════
        var topic = HbaTopics.Pour(_options, producer);

        // UN SERVICE HORS CATALOGUE PUBLIE DANS LE VIDE, ET DOIT LE DIRE.
        //
        // Le repli de `HbaTopics.Domaine` reproduit l'ancienne dérivation, donc rien
        // n'échoue : le message part sur un sujet auquel personne n'est abonné, il
        // est acquitté, et il n'arrive nulle part. C'est précisément le mode de
        // défaillance qu'on vient de fermer — il ne doit pas se rouvrir en silence
        // au prochain service ajouté.
        if (!HbaTopics.EstConnu(producer) && _catalogueIncomplet.TryAdd(producer, 0))
        {
            _logger.LogWarning(
                "Le producteur « {Producteur} » n'est pas inscrit dans HbaTopics : ses événements "
                + "partent sur « {Sujet} », auquel aucun service n'est abonné. Ajouter son domaine "
                + "à HbaTopics.DomaineParService.",
                producer, topic);
        }

        var envelope = new KafkaEventEnvelope(
            EventId: eventId,
            EventType: eventType,
            // ═════════════════════════════════════════════════════════════════
            // CE CHAMP VALAIT `1` EN DUR, DONC IL MENTAIT PAR CONSTRUCTION.
            //
            // Il était posé dans l'enveloppe ET dans l'en-tête `event-version`, et
            // le consommateur ne le lisait jamais. Le jour où quelqu'un aurait
            // changé la forme d'un événement, chaque ancien consommateur aurait
            // désérialisé la nouvelle charge EN SILENCE, champs manquants à `null`,
            // sans qu'aucune trace ne relie l'effet absent à la cause.
            //
            // La version vient désormais de `[HbaEvent].Version`, c'est-à-dire du
            // contrat lui-même. Les événements pas encore annotés valent 1 — ce qui
            // est exact : aucun n'a jamais changé de forme.
            // ═════════════════════════════════════════════════════════════════
            EventVersion: HbaEventNaming.Describe(integrationEvent.GetType())?.Version ?? 1,
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(integrationEvent.OccurredOnUtc, DateTimeKind.Utc)),
            PublishedAt: publishedAt,
            Producer: producer,
            ProducerVersion: _options.ProducerVersion ?? _configuration["SERVICE_VERSION"] ?? "dev",
            // ═════════════════════════════════════════════════════════════════
            // CETTE LIGNE LISAIT `_configuration["CorrelationId"]`, ET C'ÉTAIT
            //    UNE ERREUR DE CATÉGORIE.
            //
            // `IConfiguration` porte des réglages de démarrage, pas l'état d'une
            // requête. Cette clé n'existe nulle part et n'a jamais rien rendu : la
            // corrélation était donc TOUJOURS l'identifiant de trace, jamais le
            // `x-correlation-id` que l'utilisateur voit dans `meta.requestId`. Un
            // incident traversant trois services n'était pas reconstituable à partir
            // de ce que la personne pouvait citer.
            //
            // Le contexte ambiant est posé par `ServiceCorrelationMiddleware` sur le
            // chemin HTTP, et rétabli par `OutboxProcessor` depuis la colonne
            // `outbox_messages.CorrelationId` sur le chemin asynchrone. Les deux
            // aboutissent ici.
            //
            // Le repli sur la trace reste : un message écrit hors requête — travail
            // planifié, reprise de données — n'a légitimement pas de corrélation, et
            // une valeur cohérente vaut mieux que rien.
            // ═════════════════════════════════════════════════════════════════
            CorrelationId: PremierNonVide(
                HbaRequestContext.Current.CorrelationId,
                Activity.Current?.TraceId.ToString(),
                eventId),

            // LA CAUSALITÉ ÉTAIT CODÉE À `null` (§19.1 `causationId`).
            //
            // Elle répond à « qu'est-ce qui a provoqué cet événement ». Sans elle, on
            // sait qu'une commande et un paiement partagent une corrélation, mais pas
            // lequel a causé l'autre — et sur une chaîne de six sauts, l'ordre est
            // précisément la question qu'on se pose.
            CausationId: string.IsNullOrWhiteSpace(HbaRequestContext.Current.CausationId)
                ? null
                : HbaRequestContext.Current.CausationId,
            SagaId: null,
            AggregateType: KafkaEventNaming.AggregateType(eventType),
            AggregateId: aggregateId,
            SequenceNumber: 0,
            TenantId: KafkaEventNaming.TenantId(integrationEvent),
            Data: KafkaEventNaming.Data(integrationEvent, SerializerOptions),
            Metadata: new Dictionary<string, object?>
            {
                ["retryCount"] = 0
            });

        var value = JsonSerializer.Serialize(envelope, SerializerOptions);

        if (Encoding.UTF8.GetByteCount(value) > 256 * 1024)
        {
            throw new InvalidOperationException($"L'événement {eventType} dépasse 256 Ko. Stocker le contenu volumineux dans l'object storage et publier son URL.");
        }

        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(envelope.EventId) },
            { "event-type", Encoding.UTF8.GetBytes(envelope.EventType) },
            { "event-version", Encoding.UTF8.GetBytes(envelope.EventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
            { "correlation-id", Encoding.UTF8.GetBytes(envelope.CorrelationId) },
            { "producer", Encoding.UTF8.GetBytes(envelope.Producer) },
            { "content-type", Encoding.UTF8.GetBytes("application/json") },
            { "schema-id", Encoding.UTF8.GetBytes("0") }
        };

        if (!string.IsNullOrWhiteSpace(envelope.SagaId))
        {
            headers.Add("saga-id", Encoding.UTF8.GetBytes(envelope.SagaId));
        }

        if (Activity.Current?.Id is { Length: > 0 } traceParent)
        {
            headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
        }

        await _producer.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = aggregateId,
                Value = value,
                Headers = headers
            },
            cancellationToken);
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
    /// <summary>La première valeur non vide. Rend le repli lisible d'un coup d'œil.</summary>
    private static string PremierNonVide(params string?[] valeurs)
    {
        foreach (var valeur in valeurs)
        {
            if (!string.IsNullOrWhiteSpace(valeur))
            {
                return valeur;
            }
        }

        return string.Empty;
    }

}
