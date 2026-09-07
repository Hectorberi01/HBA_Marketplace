using System.Text.Json;
using Confluent.Kafka;

namespace HBA.Merchants.IntegrationTests;

/// <summary>Une enveloppe lue sur le courtier, réduite à ce que les tests observent.</summary>
internal sealed record Enveloppe(string EventType, string AggregateId, JsonElement Data);

/// <summary>LIRE LE COURTIER COMME UN TIERS LE LIRAIT.</summary>
internal static class BusDeTest
{
    /// <summary>
    /// Sujet de publication de seller-service : `SERVICE_NAME` vaut
    /// `merchant-service`.
    /// </summary>
    public const string SujetMerchant = "service.merchant.v1";

    /// <summary>Sujet d'identity, sur lequel les tests injectent l'anonymisation.</summary>
    public const string SujetIdentity = "service.identity.v1";

    /// <summary>LA PREMIÈRE ATTENTE EST LONGUE, LES SUIVANTES COURTES.</summary>
    private static readonly TimeSpan PremiereAttente = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan AttenteSuivante = TimeSpan.FromSeconds(2);

    /// <summary>Lit tout le sujet depuis le début.</summary>
    public static IReadOnlyList<Enveloppe> Drainer(string bootstrapServers, string sujet)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,

            // Jetable : voir l'encadré de la classe.
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

    /// <summary>Attend que <paramref name="attendu"/> messages satisfassent le filtre.</summary>
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
    /// Publie un message dans l'enveloppe que `KafkaIntegrationEventConsumer`
    /// attend.
    /// </summary>
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
            producer = "identity-service",
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
