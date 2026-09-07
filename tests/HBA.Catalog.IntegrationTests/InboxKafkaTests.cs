using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace HBA.Catalog.IntegrationTests;

/// <summary>L'INBOX DU §19.5, ÉPROUVÉE CONTRE UN VRAI COURTIER.</summary>
[Collection(CatalogIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class InboxKafkaTests
{
    /// <summary>
    /// Sujet du service vendeur :
    /// `<c>TopicPrefix</c>.merchant.<c>TopicVersion</c>`.
    /// </summary>
    private const string SujetMerchant = "service.merchant.v1";

    /// <summary>
    /// Nom de l'événement tel que `KafkaEventNaming.EventType` le calcule :
    /// `SellerClosedIntegrationEvent` → suffixe retiré → `seller.closed`.
    /// </summary>
    private const string TypeEvenement = "seller.closed";

    private const string NomConsumer = "catalog-service.merchants-seller-closed";

    private readonly CatalogIntegrationFixture _fixture;

    public InboxKafkaTests(CatalogIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>Un événement publié sur le sujet est consommé, et la trace est écrite.</summary>
    [Fact]
    public async Task Un_evenement_publie_est_consomme_et_trace_dans_l_inbox()
    {
        // Le client force la construction de l'hôte, donc le démarrage du
        // consommateur d'arrière-plan.
        _ = _fixture.CreateClient();

        var eventId = Guid.NewGuid();

        await PublierAsync(eventId, sellerId: Guid.NewGuid());

        var traces = await AttendreTracesAsync(eventId, attendu: 1);

        traces.Should().Be(1,
            "le gestionnaire doit avoir traité l'événement et écrit sa trace dans la même "
            + "unité de travail");
    }

    /// <summary>LE MÊME ÉVÉNEMENT DEUX FOIS NE DOIT PRODUIRE QU'UNE TRACE.</summary>
    [Fact]
    public async Task Le_meme_evenement_rejoue_n_est_traite_qu_une_fois()
    {
        _ = _fixture.CreateClient();

        var eventId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        await PublierAsync(eventId, sellerId);
        (await AttendreTracesAsync(eventId, attendu: 1)).Should().Be(1);

        // Même identifiant d'événement : c'est exactement ce que fait un rejeu.
        await PublierAsync(eventId, sellerId);

        // ON LAISSE VOLONTAIREMENT LE TEMPS AU DÉFAUT DE SE PRODUIRE.
        await Task.Delay(TimeSpan.FromSeconds(3));

        var traces = await CompterTracesAsync(eventId);

        traces.Should().Be(1,
            "la clé de l'inbox est le couple (événement, consumer) : un rejeu doit être ignoré");
    }

    // Outillage

    /// <summary>Publie un message dans l'enveloppe que le consommateur attend.</summary>
    private async Task PublierAsync(Guid eventId, Guid sellerId)
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            id = eventId,
            occurredOnUtc = DateTime.UtcNow,
            sellerId,
            userId = Guid.NewGuid()
        });

        var enveloppe = new
        {
            eventId = eventId.ToString(),
            eventType = TypeEvenement,
            eventVersion = 1,
            occurredAt = DateTimeOffset.UtcNow,
            publishedAt = DateTimeOffset.UtcNow,
            producer = "merchant-service",
            producerVersion = "1.0.0",
            correlationId = Guid.NewGuid().ToString(),
            causationId = (string?)null,
            sagaId = (string?)null,
            aggregateType = "seller",
            aggregateId = sellerId.ToString(),
            sequenceNumber = 1L,
            tenantId = "hba-bj",
            data,
            metadata = new Dictionary<string, object?>()
        };

        var config = new ProducerConfig { BootstrapServers = _fixture.BootstrapServers };
        using var producteur = new ProducerBuilder<string, string>(config).Build();

        await producteur.ProduceAsync(SujetMerchant, new Message<string, string>
        {
            Key = sellerId.ToString(),
            Value = JsonSerializer.Serialize(enveloppe)
        });

        producteur.Flush(TimeSpan.FromSeconds(10));
    }

    /// <summary>Attend que la trace apparaisse, ou rend le compte atteint.</summary>
    private async Task<int> AttendreTracesAsync(Guid eventId, int attendu)
    {
        var limite = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < limite)
        {
            var compte = await CompterTracesAsync(eventId);
            if (compte >= attendu)
            {
                return compte;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return await CompterTracesAsync(eventId);
    }

    private async Task<int> CompterTracesAsync(Guid eventId)
    {
        await using var connexion = new NpgsqlConnection(_fixture.ConnectionString);
        await connexion.OpenAsync();

        await using var commande = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM catalog.consumer_inbox
            WHERE "EventId" = @eventId AND "ConsumerName" = @consumer
            """,
            connexion);

        commande.Parameters.AddWithValue("eventId", eventId);
        commande.Parameters.AddWithValue("consumer", NomConsumer);

        return Convert.ToInt32(await commande.ExecuteScalarAsync());
    }
}
