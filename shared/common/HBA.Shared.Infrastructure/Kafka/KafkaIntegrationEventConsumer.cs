using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Confluent.Kafka;
using HBA.Shared.Infrastructure.Events;
using HBA.Shared.Infrastructure.Observability;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HBA.Shared.Infrastructure.Kafka;

public sealed class KafkaIntegrationEventConsumer : BackgroundService
{
    /// <summary>Le suffixe des sujets de lettres mortes : `service.identity.v1.dlq`.</summary>
    private const string SuffixeLettresMortes = ".dlq";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Types déjà signalés comme inconnus — un avertissement par type, pas par
    /// message.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _typesInconnus = new();

    /// <summary>Types ambigus déjà signalés — un avertissement par nom d'événement.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _typesAmbigus = new();

    /// <summary>Types reconnus mais sans gestionnaire, déjà signalés.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _typesSansGestionnaire = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaEventBusOptions _options;
    private readonly ILogger<KafkaIntegrationEventConsumer> _logger;

    /// <summary>Producteur de la file d'attente morte.</summary>
    private IProducer<string, string>? _producteurLettresMortes;

    public KafkaIntegrationEventConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaEventBusOptions> options,
        ILogger<KafkaIntegrationEventConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

   
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // NE PAS DÉPLACER, NE PAS SUPPRIMER.
        await Task.Yield();

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            _logger.LogInformation("Consumer Kafka désactivé ou Kafka:BootstrapServers absent.");
            return;
        }

        var group = _options.ConsumerGroup
                    ?? Environment.GetEnvironmentVariable("SERVICE_NAME")
                    ?? "hba-service";

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = group,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            TopicMetadataRefreshIntervalMs = 20_000,
            AllowAutoCreateTopics = false // UN CONSOMMATEUR NE CRÉE PAS DE SUJET, JAMAIS.
        }).Build();

        // LA MÊME TABLE QUE CELLE DU PRODUCTEUR
        var sujets = _options.SubscribeTopics is { Length: > 0 }
            ? _options.SubscribeTopics
            : HbaTopics.Tous(_options).ToArray();

        // UN SERVICE QUI DECLARE N'ECOUTER RIEN NE DEMARRE PAS DE CONSOMMATEUR.
        if (_options.AbonnementsDeclares && sujets.Length == 0)
        {
            _logger.LogInformation(
                "Aucun abonnement déclaré : ce service ne consomme aucun événement, "
                + "le consommateur Kafka ne démarre pas.");

            return;
        }

        _logger.LogInformation("Abonnement à {Nombre} sujet(s) : {Sujets}", sujets.Length, string.Join(", ", sujets));

        consumer.Subscribe(sujets);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null)
                {
                    continue;
                }

                await DispatchAvecReprisesAsync(result, stoppingToken);
                consumer.Commit(result);
            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning(ex, "Erreur Kafka pendant la consommation.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Traite un message, avec reprises bornées, sans jamais laisser une exception
    /// s'échapper.
    /// </summary>
    private async Task DispatchAvecReprisesAsync(
        ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        const int tentativesMax = 3;

        // LE `traceparent` ÉTAIT PUBLIÉ DEPUIS TOUJOURS ET LU PAR PERSONNE.
        var contexteAmont = LireContexteDeTrace(result.Message.Headers);

        using var activite = HbaTelemetry.Kafka.StartActivity(
            $"{result.Topic} process",
            ActivityKind.Consumer,
            contexteAmont ?? default,
            links: contexteAmont is { } amont ? [new ActivityLink(amont)] : null);

        activite?.SetTag("messaging.system", "kafka");
        activite?.SetTag("messaging.operation", "process");
        activite?.SetTag("messaging.destination.name", result.Topic);
        activite?.SetTag("messaging.kafka.partition", result.Partition.Value);
        activite?.SetTag("messaging.kafka.offset", result.Offset.Value);

        for (var tentative = 1; tentative <= tentativesMax; tentative++)
        {
            try
            {
                await DispatchAsync(result.Message.Value, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (tentative < tentativesMax)
                {
                    _logger.LogWarning(
                        ex,
                        "Échec du traitement (tentative {Tentative}/{Max}) — {Topic}[{Partition}]@{Offset}.",
                        tentative, tentativesMax,
                        result.Topic, result.Partition.Value, result.Offset.Value);

                    await Task.Delay(TimeSpan.FromSeconds(2 * tentative), cancellationToken);
                    continue;
                }

                // Critical : l'événement est PERDU. Aucun rejeu ne viendra, et
                // l'effet métier attendu — un profil créé, un rôle attribué —
                // n'aura pas lieu.
                _logger.LogCritical(
                    ex,
                    "ÉVÉNEMENT ABANDONNÉ après {Max} tentatives — {Topic}[{Partition}]@{Offset}. "
                    + "Son effet métier n'aura pas lieu et aucun rejeu automatique n'est prévu.",
                    tentativesMax, result.Topic, result.Partition.Value, result.Offset.Value);

                // SANS CECI, UN ÉVÉNEMENT ABANDONNÉ EST UN SPAN VERT.
                activite?.SetStatus(ActivityStatusCode.Error, "événement abandonné après reprises");
                activite?.AddException(ex);

                await MettreEnLettreMorteAsync(result, ex, cancellationToken);
            }
        }
    }

    /// <summary>LA FILE D'ATTENTE MORTE : CE QUI RESTE D'UN EVENEMENT ABANDONNE.</summary>
    private async Task MettreEnLettreMorteAsync(
        ConsumeResult<string, string> result, Exception cause, CancellationToken cancellationToken)
    {
        var sujet = $"{result.Topic}{SuffixeLettresMortes}";

        try
        {
            _producteurLettresMortes ??= new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,

                // `Acks.All` ET PAS MOINS. Un message qu'on met en lettre morte a
                // deja ete perdu une fois ; accepter qu'il le soit une seconde pour
                // gagner quelques millisecondes n'aurait aucun sens.
                Acks = Acks.All,
                EnableIdempotence = true,
                AllowAutoCreateTopics = false
            }).Build();

            var entetes = new Headers();
            foreach (var entete in result.Message.Headers)
            {
                // `Headers.Add` NE PREND PAS UN `IHeader`, ET L'ITERATION EN REND
                // UN.
                entetes.Add(entete.Key, entete.GetValueBytes());
            }

            entetes.Add("dlq-sujet-origine", Encoding.UTF8.GetBytes(result.Topic));
            entetes.Add("dlq-partition-origine", Encoding.UTF8.GetBytes(result.Partition.Value.ToString()));
            entetes.Add("dlq-offset-origine", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()));
            entetes.Add("dlq-horodatage", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")));

            // LE MESSAGE D'ERREUR, TRONQUE. Une pile complete dans un en-tete Kafka
            // gonfle chaque message et se lit mal ; le type et le message suffisent
            // a orienter, la pile est dans le journal `Critical`.
            var raison = $"{cause.GetType().Name}: {cause.Message}";
            entetes.Add("dlq-raison", Encoding.UTF8.GetBytes(raison.Length <= 500 ? raison : raison[..500]));

            await _producteurLettresMortes.ProduceAsync(
                sujet,
                new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = entetes
                },
                cancellationToken);

            _logger.LogWarning(
                "Événement abandonné recopié sur {SujetDlq} — origine {Topic}[{Partition}]@{Offset}.",
                sujet, result.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (Exception echec)
        {
            _logger.LogCritical(
                echec,
                "MISE EN LETTRE MORTE IMPOSSIBLE sur {SujetDlq} : l'événement "
                + "{Topic}[{Partition}]@{Offset} est définitivement perdu, sans copie. "
                + "Vérifier que le sujet existe — il n'est pas créé automatiquement.",
                sujet, result.Topic, result.Partition.Value, result.Offset.Value);
        }
    }

    public override void Dispose()
    {
        _producteurLettresMortes?.Flush(TimeSpan.FromSeconds(5));
        _producteurLettresMortes?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Reconstitue le contexte de trace depuis l'en-tête `traceparent` du message.
    /// </summary>
    private static ActivityContext? LireContexteDeTrace(Headers? headers)
    {
        if (headers is null)
        {
            return null;
        }

        foreach (var header in headers)
        {
            if (!string.Equals(header.Key, "traceparent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var valeur = Encoding.UTF8.GetString(header.GetValueBytes());

            return ActivityContext.TryParse(valeur, traceState: null, out var contexte)
                ? contexte
                : null;
        }

        return null;
    }

    private async Task DispatchAsync(string value, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<KafkaEventEnvelope>(value, SerializerOptions);
        if (envelope is null)
        {
            return;
        }

        var eventType = ResolveEventType(envelope.EventType);
        if (eventType is null)
        {
            // CE `return` ÉTAIT MUET, ET C'EST CE QUI A COÛTÉ LE PLUS CHER.
            if (_typesInconnus.TryAdd(envelope.EventType, 0))
            {
                _logger.LogWarning(
                    "Événement « {EventType} » reçu et NON RECONNU : aucun type chargé ne lui "
                    + "correspond. Si ce service est censé le traiter, il lui manque la référence "
                    + "au projet *.Contracts qui le déclare — sans elle l'assembly n'est jamais "
                    + "chargée et l'événement est ignoré en silence.",
                    envelope.EventType);
            }

            return;
        }

        // ON REFUSE UNE VERSION QU'ON NE SAIT PAS LIRE, AU LIEU DE DEVINER (D32).
        var versionConnue = HbaEventNaming.Describe(eventType)?.Version ?? 1;

        if (envelope.EventVersion > versionConnue)
        {
            _logger.LogCritical(
                "Événement « {EventType} » reçu en version {Recue}, ce service ne sait lire que la "
                + "version {Connue} : IGNORÉ, et son effet métier n'aura pas lieu. La convention du "
                + "dépôt est additive — une rupture doit créer un NOUVEAU type d'événement, pas une "
                + "version supérieure du même. Vérifier le producteur.",
                envelope.EventType, envelope.EventVersion, versionConnue);

            return;
        }

        var integrationEvent = JsonSerializer.Deserialize(envelope.Data.GetRawText(), eventType, SerializerOptions) as IntegrationEvent;
        if (integrationEvent is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToArray();

        if (handlers.Length == 0)
        {
            // CE CAS ÉTAIT EN `Debug`, DONC INVISIBLE EN EXPLOITATION.
            if (_typesSansGestionnaire.TryAdd(envelope.EventType, 0))
            {
                _logger.LogInformation(
                    "Événement « {EventType} » reconnu ici, mais AUCUN GESTIONNAIRE n'est "
                    + "enregistré : il est acquitté sans effet. Normal si ce service n'est pas "
                    + "concerné ; sinon il manque un AddScoped<IIntegrationEventHandler<{Type}>, …> "
                    + "dans son ModuleInstaller — et les messages déjà lus ne reviendront pas.",
                    envelope.EventType, eventType.Name);
            }

            return;
        }

        _logger.LogInformation(
            "Événement « {EventType} » traité par {Count} gestionnaire(s).",
            envelope.EventType, handlers.Length);

        // LA CORRÉLATION ENTRE ICI, OU ELLE N'ENTRE NULLE PART (§11 gRPC).
        using var correlation = HbaRequestContext.BeginScope(new HbaRequestContext
        {
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            CausationId = envelope.EventId,
            TraceId = Activity.Current?.TraceId.ToString()
        });

        var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();
        await dispatcher.DispatchAsync(integrationEvent, cancellationToken);
    }

    /// <summary>Retrouve le type .NET correspondant au nom porté par l'enveloppe.</summary>
    private Type? ResolveEventType(string eventType)
    {
        var candidats = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(static assembly =>
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(type => type is not null)!;
                }
            })
            .Where(type =>
                type is not null
                && !type.IsAbstract
                && typeof(IntegrationEvent).IsAssignableFrom(type)
                && KafkaEventNaming.EventType(type) == eventType)
            .OrderBy(type => type!.FullName, StringComparer.Ordinal)
            .ToArray();

        if (candidats.Length > 1 && _typesAmbigus.TryAdd(eventType, 0))
        {
            _logger.LogWarning(
                "Événement « {EventType} » : {Nombre} types y répondent — {Types}. Le premier par "
                + "ordre alphabétique est retenu. Un gestionnaire enregistré pour un AUTRE de ces "
                + "types ne serait jamais appelé, sans erreur.",
                eventType, candidats.Length,
                string.Join(", ", candidats.Select(t => t!.FullName)));
        }

        return candidats.FirstOrDefault();
    }
}
