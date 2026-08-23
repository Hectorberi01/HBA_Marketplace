using System.Text.Json;
using Confluent.Kafka;

namespace HBA.Order.IntegrationTests;

/// <summary>Une enveloppe lue sur le courtier, réduite à ce que les tests observent.</summary>
internal sealed record Enveloppe(string EventType, string AggregateId, JsonElement Data);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// PARLER AU COURTIER COMME PAYMENT-SERVICE LUI PARLE, ET LE LIRE COMME UN TIERS
/// LE LIRAIT.
///
/// LES DEUX NOMS DE SUJET CI-DESSOUS NE SE DEVINENT PAS : ILS SORTENT DE
///    `HbaTopics.DomaineParService`.
///
/// Le sujet est `{TopicPrefix}.{domaine}.{TopicVersion}` — par défaut
/// `service.{domaine}.v1` (voir `KafkaEventBusOptions`). Et le DOMAINE n'est pas
/// le nom du conteneur : la table le traduit, ligne par ligne, et c'est tout
/// l'objet d'ISSUE-001.
///
///     payment-service  →  financial   →  service.financial.v1
///     order-service    →  order       →  service.order.v1
///
/// La première traduction est celle qui manquait. Le producteur dérivait son
/// sujet du `SERVICE_NAME` en retirant « -service » : payment-service publiait
/// donc sur `service.payment.v1` quand order-service écoutait
/// `service.financial.v1`. Un message part, il est acquitté, et il n'arrive
/// nulle part — sans erreur, sans avertissement. C'est très exactement ISSUE-002
/// et ISSUE-003.
///
/// ÉCRITS À LA MAIN, ET NON CALCULÉS PAR `HbaTopics.Pour`.
///
/// Appeler la table ici ferait passer ce test quoi qu'elle contienne : si
/// quelqu'un remettait `payment-service` → `payment`, le test suivrait
/// docilement, publierait sur `service.payment.v1`, et resterait vert pendant que
/// la plateforme cesserait d'encaisser. Un nom de sujet est un CONTRAT entre deux
/// services ; il s'écrit à la main dans un test, précisément pour qu'il faille
/// venir le modifier ici — et se demander pourquoi.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class BusDeTest
{
    /// <summary>
    /// Le sujet de payment-service. `HbaTopics.DomaineParService` traduit
    /// `payment-service` → `financial` ; wallet et billing partagent le même hôte
    /// et donc le même sujet.
    /// </summary>
    public const string SujetFinancial = "service.financial.v1";

    /// <summary>
    /// Le sujet d'order-service : `order-service` → `order`, une des entrées où
    /// le nom du conteneur coïncide déjà avec le domaine.
    /// </summary>
    public const string SujetOrder = "service.order.v1";

    /// <summary>
    /// LA PREMIÈRE ATTENTE EST LONGUE, LES SUIVANTES COURTES.
    ///
    /// Le premier `Consume` d'un groupe neuf couvre la découverte du coordinateur
    /// et le rééquilibrage initial — plusieurs secondes sur une machine chargée.
    /// Les suivants ne font que lire. Une seule valeur pour les deux serait soit
    /// instable, soit inutilement lente sur chaque message.
    /// </summary>
    private static readonly TimeSpan PremiereAttente = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan AttenteSuivante = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Lit tout le sujet depuis le début.
    /// </summary>
    /// <remarks>
    /// CHAQUE LECTURE PART DU DÉBUT, AVEC UN GROUPE NEUF.
    ///
    /// Réutiliser un groupe ferait dépendre chaque appel des offsets committés par
    /// le précédent : deux tests lisant `service.order.v1` se voleraient leurs
    /// messages, et l'échec dépendrait de l'ordre d'exécution. Un groupe jetable
    /// par lecture rend les tests indépendants — au prix de relire tout le sujet,
    /// ce qui, sur quelques dizaines de messages, ne se mesure pas.
    ///
    /// ET ON N'INTERROGE PAS `outbox_messages`.
    ///
    /// Vérifier qu'une ligne d'outbox existe prouve seulement que le gestionnaire
    /// de domaine a tourné — ce que les tests unitaires savent déjà faire. Pire,
    /// la table est un observable INSTABLE : le processeur marque puis nettoie ses
    /// lignes traitées, donc l'assertion dépendrait du moment où l'on regarde. Le
    /// courtier, lui, conserve.
    /// </remarks>
    public static IReadOnlyList<Enveloppe> Drainer(string bootstrapServers, string sujet)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,

            // Jetable : voir l'encadré de cette méthode.
            GroupId = $"test-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,

            // Sans quoi la souscription à un sujet encore inexistant lève au lieu
            // de rendre simplement zéro message.
            AllowAutoCreateTopics = true
        };

        using var consommateur = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, _) => { })
            .Build();

        consommateur.Subscribe(sujet);

        var enveloppes = new List<Enveloppe>();
        var attente = PremiereAttente;

        try
        {
            while (true)
            {
                var resultat = consommateur.Consume(attente);
                attente = AttenteSuivante;

                if (resultat?.Message?.Value is null)
                {
                    break;
                }

                if (Lire(resultat.Message.Value) is { } enveloppe)
                {
                    enveloppes.Add(enveloppe);
                }
            }
        }
        finally
        {
            consommateur.Close();
        }

        return enveloppes;
    }

    /// <summary>
    /// Attend que <paramref name="attendu"/> messages satisfassent le filtre.
    /// </summary>
    /// <remarks>
    /// UNE ATTENTE ACTIVE, PAS UN `Task.Delay` FIXE.
    ///
    /// Le processeur d'outbox scrute toutes les CINQ secondes, et le rééquilibrage
    /// initial du courtier s'y ajoute. Un délai fixe assez court rend le test
    /// instable ; assez long, il ralentit toute la suite. On sonde, et l'on
    /// s'arrête dès que c'est bon.
    ///
    /// Un test instable est pire qu'un test absent : on finit par le désactiver, et
    /// par désactiver ses voisins avec lui.
    /// </remarks>
    public static async Task<IReadOnlyList<Enveloppe>> AttendreAsync(
        string bootstrapServers,
        string sujet,
        Func<Enveloppe, bool> filtre,
        int attendu,
        TimeSpan? limite = null)
    {
        var echeance = DateTime.UtcNow + (limite ?? TimeSpan.FromSeconds(90));
        IReadOnlyList<Enveloppe> retenus = Array.Empty<Enveloppe>();

        while (DateTime.UtcNow < echeance)
        {
            retenus = (await Task.Run(() => Drainer(bootstrapServers, sujet)))
                .Where(filtre)
                .ToList();

            if (retenus.Count >= attendu)
            {
                return retenus;
            }
        }

        return retenus;
    }

    /// <summary>
    /// Publie un message dans l'enveloppe que `KafkaIntegrationEventConsumer` attend.
    /// </summary>
    /// <remarks>
    /// `Id` DOIT ÊTRE DANS `data`, PAS SEULEMENT DANS `eventId`.
    ///
    /// C'est `IntegrationEvent.Id` — donc une propriété de la CHARGE UTILE — que
    /// le dispatcher passe à l'inbox. Le `eventId` de l'enveloppe ne le renseigne
    /// pas (il porte d'ailleurs un ULID `evt_…` chez le vrai producteur, pas un
    /// GUID) : sans `id` dans `data`, chaque rejeu recevrait un identifiant neuf à
    /// la désérialisation et l'inbox ne reconnaîtrait jamais rien. Le test
    /// d'idempotence passerait alors pour la pire des raisons — il n'éprouverait
    /// plus la garde, seulement la capacité du courtier à livrer deux fois.
    ///
    /// LA CLÉ EST L'AGRÉGAT, comme chez le vrai producteur : `KafkaEventNaming`
    /// retient `OrderId` pour les événements de paiement. C'est ce qui garantit
    /// que capture et échec d'une même commande restent ordonnés dans une
    /// partition.
    /// </remarks>
    public static async Task PublierAsync(
        string bootstrapServers,
        string sujet,
        Guid eventId,
        string typeEvenement,
        string aggregateType,
        string aggregateId,
        object charge)
    {
        var data = JsonSerializer.SerializeToElement(charge);

        var enveloppe = new
        {
            eventId = eventId.ToString(),
            eventType = typeEvenement,
            eventVersion = 1,
            occurredAt = DateTimeOffset.UtcNow,
            publishedAt = DateTimeOffset.UtcNow,
            producer = "payment-service",
            producerVersion = "1.0.0",
            correlationId = Guid.NewGuid().ToString(),
            causationId = (string?)null,
            sagaId = (string?)null,
            aggregateType,
            aggregateId,
            sequenceNumber = 1L,
            tenantId = "hba-bj",
            data,
            metadata = new Dictionary<string, object?>()
        };

        using var producteur = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = bootstrapServers }).Build();

        await producteur.ProduceAsync(sujet, new Message<string, string>
        {
            Key = aggregateId,
            Value = JsonSerializer.Serialize(enveloppe)
        });

        producteur.Flush(TimeSpan.FromSeconds(10));
    }

    private static Enveloppe? Lire(string charge)
    {
        try
        {
            using var document = JsonDocument.Parse(charge);
            var racine = document.RootElement;

            var type = racine.TryGetProperty("eventType", out var t) ? t.GetString() : null;

            if (type is null)
            {
                return null;
            }

            var agregat = racine.TryGetProperty("aggregateId", out var a) ? a.GetString() : null;

            var data = racine.TryGetProperty("data", out var d) ? d.Clone() : default;

            return new Enveloppe(type, agregat ?? string.Empty, data);
        }
        catch (JsonException)
        {
            // Un message illisible n'est pas l'objet de ces tests : on l'ignore
            // plutôt que de faire échouer une lecture sur du bruit voisin.
            return null;
        }
    }
}
