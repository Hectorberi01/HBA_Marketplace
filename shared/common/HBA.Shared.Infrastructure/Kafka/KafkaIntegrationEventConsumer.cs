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
    /// <summary>
    /// Le suffixe des sujets de lettres mortes : `service.identity.v1.dlq`.
    ///
    /// Suffixe et non prefixe : les outils d'exploitation listent les sujets par
    /// ordre alphabetique, et un sujet mort reste ainsi colle a son sujet vivant.
    /// </summary>
    private const string SuffixeLettresMortes = ".dlq";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Types déjà signalés comme inconnus — un avertissement par type, pas par message.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _typesInconnus = new();

    /// <summary>Types ambigus déjà signalés — un avertissement par nom d'événement.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _typesAmbigus = new();

    /// <summary>
    /// Types reconnus mais sans gestionnaire, déjà signalés. Un message par nom
    /// d'événement : chaque service reçoit les événements des douze autres, en
    /// journaliser un par MESSAGE rendrait le journal illisible.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _typesSansGestionnaire = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaEventBusOptions _options;
    private readonly ILogger<KafkaIntegrationEventConsumer> _logger;

    /// <summary>
    /// Producteur de la file d'attente morte. Cree a la PREMIERE mise en lettre
    /// morte, et pas au demarrage : la plupart des services n'en auront jamais
    /// besoin, et une connexion ouverte pour rien est une connexion a surveiller.
    /// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // UN SERVICE QUI DECLARE N'ECOUTER RIEN NE DEMARRE PAS DE CONSOMMATEUR.
        //
        // La ligne au-dessus lisait « liste vide » comme « pas de liste », et
        // s'abonnait donc a tout. Sept services sans aucun gestionnaire —
        // billing, driver, inventory, recommendation, return-refund, review,
        // wishlist — deserialisaient ainsi l'integralite du trafic du bus pour le
        // jeter aussitot.
        //
        // `AbonnementsDeclares` distingue les deux cas. Un service qui n'a pas
        // encore de module Kafka garde l'ancien comportement.
        //
        // CE QUE ÇA NE COUVRE PAS. Le producteur, lui, reste enregistre : ces
        // services publient toujours. Seule la boucle de consommation s'arrete.
        // ═════════════════════════════════════════════════════════════════════
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
    /// Traite un message, avec reprises bornées, sans jamais laisser une
    /// exception s'échapper.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE EXCEPTION QUI S'ÉCHAPPE D'ICI ABAT LE SERVICE ENTIER.
    ///
    /// `HostOptions.BackgroundServiceExceptionBehavior` vaut `StopHost` par
    /// défaut : une exception non rattrapée dans `ExecuteAsync` arrête l'hôte.
    /// La boucle ne rattrapait que `ConsumeException` — les erreurs venant des
    /// GESTIONNAIRES passaient au travers.
    ///
    /// Constaté en vrai : un appel gRPC refusé par l'intercepteur interne, dans
    /// un gestionnaire de création de profil, et user-service s'est arrêté. Un
    /// service qui meurt parce qu'UN événement s'est mal passé n'est pas
    /// acceptable — d'autant que les gestionnaires lèvent volontairement pour
    /// signaler qu'il faut réessayer.
    ///
    /// TROIS TENTATIVES, PUIS ON ABANDONNE BRUYAMMENT.
    ///
    /// Les pannes visées sont transitoires : un service amont qui redémarre, une
    /// base momentanément indisponible. Trois essais espacés couvrent ça.
    /// Au-delà, la cause est structurelle — une clé mal configurée, un contrat
    /// incompatible — et réessayer indéfiniment ne ferait que masquer le
    /// problème derrière un flux à l'arrêt.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private async Task DispatchAvecReprisesAsync(
        ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        const int tentativesMax = 3;

        // ═════════════════════════════════════════════════════════════════════
        // LE `traceparent` ÉTAIT PUBLIÉ DEPUIS TOUJOURS ET LU PAR PERSONNE.
        //
        // `KafkaIntegrationEventPublisher` pose un en-tête `traceparent` sur chaque
        // message depuis l'origine. Sans les trois lignes ci-dessous, il ne servait
        // à rien : la trace du producteur s'arrêtait à `SaveChanges`, et le travail
        // du consommateur ouvrait — au mieux — une trace ORPHELINE, sans lien avec
        // la requête qui l'avait causé.
        //
        // Ce que cela coûtait concrètement : une commande passée produit huit
        // effets asynchrones — stock réservé, paiement, notification, course créée.
        // Aucun n'apparaissait sous la trace de la commande. Pour comprendre
        // pourquoi une course n'est pas partie, il fallait recouper des
        // horodatages entre quatre services.
        //
        // `AddLink` EN PLUS DU PARENT, ET CE N'EST PAS REDONDANT.
        //
        // Le lien de parenté rattache ce span à la requête d'origine ; le lien
        // explicite survit à un échantillonnage qui écarterait la trace parente.
        // Sur un flux à fort volume — et les événements le sont — c'est la
        // différence entre « je retrouve la cause » et « la trace parente a été
        // écartée ».
        // ═════════════════════════════════════════════════════════════════════
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
                // n'aura pas lieu. C'est le genre de trou qu'on ne découvre
                // sinon qu'en regardant une table vide.
                _logger.LogCritical(
                    ex,
                    "ÉVÉNEMENT ABANDONNÉ après {Max} tentatives — {Topic}[{Partition}]@{Offset}. "
                    + "Son effet métier n'aura pas lieu et aucun rejeu automatique n'est prévu.",
                    tentativesMax, result.Topic, result.Partition.Value, result.Offset.Value);

                // SANS CECI, UN ÉVÉNEMENT ABANDONNÉ EST UN SPAN VERT.
                //
                // La méthode ne relève pas l'exception — c'est voulu, l'offset doit
                // avancer — donc rien ne marque l'activité en échec. Dans Grafana, la
                // perte définitive d'un effet métier ressemblerait alors à un
                // traitement réussi, et aucune alerte fondée sur le taux d'erreur ne
                // se déclencherait. C'est le journal Critical qui porte l'information,
                // et il ne se corrèle à rien.
                activite?.SetStatus(ActivityStatusCode.Error, "événement abandonné après reprises");
                activite?.AddException(ex);

                await MettreEnLettreMorteAsync(result, ex, cancellationToken);
            }
        }
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA FILE D'ATTENTE MORTE : CE QUI RESTE D'UN EVENEMENT ABANDONNE.
    ///
    /// CE QUI SE PASSAIT. Apres trois tentatives, le message etait perdu. Pas
    /// retarde : PERDU. L'offset avançait, l'evenement disparaissait du sujet a
    /// l'expiration de la retention, et la seule trace etait une ligne de journal
    /// `Critical` que personne ne relit. C'est par ce trou que le
    /// `user.registered` du 30 aout est parti — il n'en reste rien a rejouer.
    ///
    /// CE QUE ÇA CHANGE. Le message est recopie TEL QUEL sur `{sujet}.dlq`, avec
    /// ses en-tetes d'origine et cinq de plus qui disent d'ou il vient et pourquoi
    /// il est la. Il devient inspectable, comptable, et surtout REJOUABLE : le
    /// remettre sur son sujet d'origine est une commande, plus une reconstitution.
    ///
    /// LE SUJET N'EST PAS CREE AUTOMATIQUEMENT, ET C'EST UN PREREQUIS
    ///     D'EXPLOITATION.
    ///
    /// Le consommateur pose `AllowAutoCreateTopics = false` par principe, et un
    /// courtier de production refuse en general la creation implicite. Les sujets
    /// `*.dlq` doivent donc etre provisionnes comme les autres. S'ils manquent,
    /// la production echoue — et l'echec est journalise en `Critical` plutot
    /// qu'avale : un message perdu qu'on croit sauve est pire qu'un message perdu.
    ///
    /// CE QUE ÇA NE COUVRE PAS. Rien ne relit ces sujets aujourd'hui : il n'y a
    /// ni consommateur d'archivage, ni tableau de bord, ni alerte sur leur
    /// profondeur. La perte devient visible, elle n'est pas encore surveillee.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
                // deja ete perdu une fois ; accepter qu'il le soit une seconde
                // pour gagner quelques millisecondes n'aurait aucun sens.
                Acks = Acks.All,
                EnableIdempotence = true,
                AllowAutoCreateTopics = false
            }).Build();

            var entetes = new Headers();
            foreach (var entete in result.Message.Headers)
            {
                // `Headers.Add` NE PREND PAS UN `IHeader`, ET L'ITERATION EN REND UN.
                //
                // Les surcharges sont `(string, byte[])` et `(Header)` — un type
                // concret. Parcourir `Message.Headers` donne des `IHeader` : il faut
                // donc redonner la cle et les octets, pas repasser l'objet.
                entetes.Add(entete.Key, entete.GetValueBytes());
            }

            entetes.Add("dlq-sujet-origine", Encoding.UTF8.GetBytes(result.Topic));
            entetes.Add("dlq-partition-origine", Encoding.UTF8.GetBytes(result.Partition.Value.ToString()));
            entetes.Add("dlq-offset-origine", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()));
            entetes.Add("dlq-horodatage", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")));

            // LE MESSAGE D'ERREUR, TRONQUE. Une pile complete dans un en-tete
            // Kafka gonfle chaque message et se lit mal ; le type et le message
            // suffisent a orienter, la pile est dans le journal `Critical`.
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
    /// <remarks>
    /// RENVOIE `null` PLUTÔT QU'UN CONTEXTE VIDE, ET LA DIFFÉRENCE COMPTE.
    ///
    /// `ActivityContext.TryParse` sur un en-tête absent ou malformé rendrait un
    /// contexte à zéro. Passé comme parent, il ne produit pas une trace racine
    /// propre : il produit un span rattaché à un identifiant nul, que les
    /// collecteurs traitent diversement — certains le jettent. En rendant `null`,
    /// on laisse `StartActivity` créer une racine franche, ce qui est le
    /// comportement juste pour un message publié hors de toute requête (un travail
    /// planifié, une reprise).
    ///
    /// Les messages d'avant ce lot n'ont pas d'en-tête `traceparent` du tout. Ils
    /// doivent continuer de passer, en racine — d'où l'absence de journal ici : ce
    /// n'est pas une anomalie.
    /// </remarks>
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
            // ═════════════════════════════════════════════════════════════════
            // CE `return` ÉTAIT MUET, ET C'EST CE QUI A COÛTÉ LE PLUS CHER.
            //
            // Chaque service s'abonne aux TREIZE sujets : la grande majorité des
            // messages ne le concernent pas, et les ignorer est le comportement
            // normal. D'où le silence d'origine.
            //
            // Mais il confondait deux situations opposées :
            //
            //   • « je ne connais pas ce type » — le projet `*.Contracts` qui le
            //     déclare n'est pas référencé, donc son assembly n'est pas
            //     chargée. C'est un DÉFAUT DE CÂBLAGE.
            //   • « je le connais, mais je n'ai pas de gestionnaire » — cas
            //     parfaitement normal, douze fois sur treize.
            //
            // Le premier ressemble au second, et ne produisait rien : pas de
            // journal, pas d'erreur, et l'offset était committé juste après. On
            // découvre le trou en regardant une table vide.
            //
            // Une seule fois par type : sinon un service noierait ses journaux
            // sous les événements des douze autres.
            // ═════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // ON REFUSE UNE VERSION QU'ON NE SAIT PAS LIRE, AU LIEU DE DEVINER (D32).
        //
        // La convention du dépôt est ADDITIVE : on n'ajoute que des champs
        // optionnels, et une rupture crée un NOUVEAU type d'événement, jamais une
        // version 2 du même. Une version entrante supérieure à celle que ce service
        // connaît signifie donc que la règle a été enfreinte quelque part.
        //
        // Sans ce contrôle, `JsonSerializer` ferait ce qu'il fait toujours : lire ce
        // qu'il reconnaît, ignorer le reste, et rendre un objet aux champs manquants
        // à `null`. Le gestionnaire s'exécuterait sur une charge amputée, écrirait
        // un effet faux, et la seule trace serait un span vert. C'est le mode de
        // défaillance le plus coûteux de cette architecture, et il se ferme ici en
        // dix lignes.
        //
        // ON ACQUITTE MALGRÉ TOUT, et c'est un arbitrage, pas un oubli. Bloquer
        // l'offset mettrait à l'arrêt TOUS les autres événements de ce producteur —
        // le paiement, la commande, la livraison — pour un message qui viole une
        // convention. La perte d'un message annoncée en `Critical` est un moindre
        // mal que l'arrêt d'une partition. C'est le même arbitrage que pour
        // l'abandon après trois tentatives, quelques lignes plus bas.
        // ═════════════════════════════════════════════════════════════════════
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
            // ═════════════════════════════════════════════════════════════════
            // CE CAS ÉTAIT EN `Debug`, DONC INVISIBLE EN EXPLOITATION.
            //
            // « Type reconnu, aucun gestionnaire » est effectivement le cas
            // NORMAL douze fois sur treize — d'où le niveau d'origine. Mais il
            // recouvre aussi le cas ANORMAL : le service devrait traiter cet
            // événement et l'enregistrement manque. Les deux se ressemblent,
            // et le second ne produisait rien du tout.
            //
            // Le typage a d'ailleurs été fait ici : le projet `*.Contracts` est
            // référencé, l'assembly est chargée, le type résout. Si personne ne
            // l'écoute alors qu'on est allé jusqu'à référencer son contrat,
            // c'est très probablement un oubli d'enregistrement.
            //
            // ET L'OFFSET EST COMMITTÉ JUSTE APRÈS (voir la boucle).
            //
            // Conséquence à connaître, parce qu'elle mord : un événement lu
            // AVANT que son gestionnaire n'existe est acquitté pour ce groupe
            // de consommateurs, et ne sera JAMAIS redélivré. Ajouter le
            // gestionnaire plus tard ne rattrape rien — il faut republier
            // l'événement, ou remettre l'offset du groupe à zéro.
            //
            // Information, et une seule fois par type : assez pour se voir dans
            // un `docker compose logs`, pas assez pour noyer le journal sous
            // les événements des douze autres services.
            // ═════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // LA CORRÉLATION ENTRE ICI, OU ELLE N'ENTRE NULLE PART (§11 gRPC).
        //
        // L'enveloppe la porte depuis le producteur, et l'en-tête Kafka aussi. Mais
        // rien ne l'installait dans le contexte ambiant : les gestionnaires qui
        // lisent `HbaRequestContext.Current.CorrelationId` — ceux de l'inbox, ceux
        // du journal d'audit — obtenaient une chaîne vide. La colonne
        // `consumer_inbox.CorrelationId` existait et recevait `null`, alors que
        // l'information était dans le message qu'on venait de lire.
        //
        // ET LA CAUSALITÉ : ce qui sera publié par un gestionnaire a pour CAUSE
        // l'événement reçu. `CausationId` prend donc son identifiant — c'est ce qui
        // permet de reconstituer l'ORDRE d'une chaîne, là où la corrélation seule ne
        // dit que l'appartenance.
        //
        // Le scope se referme avec le message : `BeginScope` restaure la valeur
        // précédente, donc un message ne laisse pas son contexte au suivant.
        // ═════════════════════════════════════════════════════════════════════
        using var correlation = HbaRequestContext.BeginScope(new HbaRequestContext
        {
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            CausationId = envelope.EventId,
            TraceId = Activity.Current?.TraceId.ToString()
        });

        var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();
        await dispatcher.DispatchAsync(integrationEvent, cancellationToken);
    }

    /// <summary>
    /// Retrouve le type .NET correspondant au nom porté par l'enveloppe.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// PLUSIEURS ASSEMBLIES PEUVENT DÉCLARER LE MÊME ÉVÉNEMENT.
    ///
    /// `OrderConfirmedIntegrationEvent` existe deux fois : dans le contrat
    /// PARTAGÉ (`HBA.Ordering.Contracts`) et dans celui d'order-service
    /// (`HBA.Orders.Contracts`). L'enveloppe Kafka ne transporte que le NOM
    /// — « order.confirmed » — pas l'espace de noms. Les deux types y répondent.
    ///
    /// Avec un simple `FirstOrDefault`, le vainqueur dépend de l'ORDRE DE
    /// CHARGEMENT des assemblies : non déterministe, variable d'un démarrage à
    /// l'autre. Et si le gestionnaire est enregistré pour l'autre type,
    /// `GetServices` n'en trouve aucun et l'événement passe SANS EFFET, en
    /// silence — la panne exacte qu'on a passé la journée à traquer.
    ///
    /// On rend donc le choix déterministe — tri par nom complet — et on le
    /// SIGNALE une fois par type. Le duplicata est une dette à résorber ; en
    /// attendant, il est au moins visible et reproductible.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
