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

    /// <summary>
    /// Producteurs hors catalogue déjà signalés — un avertissement, pas un par
    /// message.
    /// </summary>
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
            // sa vie.
            _logger.LogCritical(
                "Producteur Kafka NON CONSTRUIT ({Cause}). Ce service ne publiera aucun "
                + "événement d'intégration tant qu'il vivra.",
                _options.Enabled ? "Kafka:BootstrapServers absent" : "Kafka:Enabled=false");
        }
    }

    /// <summary>ON LÈVE QUAND LE PRODUCTEUR MANQUE. ON NE REND PLUS LA MAIN EN SUCCÈS.</summary>
    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (_producer is null)
        {
            throw new InvalidOperationException(
                $"Événement « {integrationEvent.GetType().Name} » NON PUBLIABLE : le producteur "
                + "Kafka n'existe pas ("
                + (_options.Enabled ? "Kafka:BootstrapServers absent" : "Kafka:Enabled=false")
                + "). Le message reste dans l'outbox et finira en lettre morte. "
                + "Renseigner Kafka:BootstrapServers, puis remettre les lignes en file à la main "
                + "(DeadLetteredOnUtc = NULL, AttemptCount = 0, NextAttemptAtUtc = NULL) : "
                + "il n'existe aucune route de rejeu.");
        }

        var producer = KafkaEventNaming.Producer(_options.Producer, _configuration["SERVICE_NAME"]);
        var eventType = KafkaEventNaming.EventType(integrationEvent.GetType());
        var aggregateId = KafkaEventNaming.AggregateId(integrationEvent);
        var eventId = KafkaEventNaming.UlidFrom(integrationEvent.Id, integrationEvent.OccurredOnUtc);
        var publishedAt = DateTimeOffset.UtcNow;
        // LE SUJET VIENT DU CATALOGUE, PLUS D'UNE DÉRIVATION DU NOM DU CONTENEUR.
        var topic = HbaTopics.Pour(_options, producer);

        // UN SERVICE HORS CATALOGUE PUBLIE DANS LE VIDE, ET DOIT LE DIRE.
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
            // CE CHAMP VALAIT `1` EN DUR, DONC IL MENTAIT PAR CONSTRUCTION.
            EventVersion: HbaEventNaming.Describe(integrationEvent.GetType())?.Version ?? 1,
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(integrationEvent.OccurredOnUtc, DateTimeKind.Utc)),
            PublishedAt: publishedAt,
            Producer: producer,
            ProducerVersion: _options.ProducerVersion ?? _configuration["SERVICE_VERSION"] ?? "dev",
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
            CorrelationId: PremierNonVide(
                HbaRequestContext.Current.CorrelationId,
                Activity.Current?.TraceId.ToString(),
                eventId),

            // LA CAUSALITÉ ÉTAIT CODÉE À `null` (§19.1 `causationId`).
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
    /// <summary>La première valeur non vide.</summary>
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
