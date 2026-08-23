using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace HBA.Catalog.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'INBOX DU §19.5, ÉPROUVÉE CONTRE UN VRAI COURTIER.
///
/// C'EST LE TEST QUE LE README DE `tests/integration` RÉCLAMAIT DEPUIS LE DÉBUT.
///
/// Son texte : « C'est le niveau qui remplace ce qu'un test unitaire garantissait
/// gratuitement dans le monolithe : qu'un événement publié est bien reçu. » Dans
/// un seul processus, publier et consommer étaient un appel de méthode. Découpés,
/// ce sont quatre endroits où le lien peut se rompre — le nom du sujet, le nom de
/// l'événement, la forme de l'enveloppe, l'enregistrement du gestionnaire — et
/// AUCUN ne casse la compilation.
///
/// Le dépôt a déjà payé ce silence : un consommateur perdu à l'extraction de
/// user-service, un événement produit consciencieusement et écouté par personne.
///
/// CE QUE CES DEUX TESTS FIXENT, ET QUI VIENT DU LOT 6.
///
/// L'inbox a été branchée sur catalog sans qu'aucun test ne l'exerce : les deux
/// gestionnaires sont naturellement idempotents, donc un rejeu ne se VOIT pas dans
/// les données. C'est ici, et seulement ici, que la garde elle-même est observable
/// — par la ligne qu'elle écrit dans `consumer_inbox`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(CatalogIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class InboxKafkaTests
{
    /// <summary>Sujet du service vendeur : `<c>TopicPrefix</c>.merchant.<c>TopicVersion</c>`.</summary>
    private const string SujetMerchant = "service.merchant.v1";

    /// <summary>
    /// Nom de l'événement tel que `KafkaEventNaming.EventType` le calcule :
    /// `SellerClosedIntegrationEvent` → suffixe retiré → `seller.closed`.
    ///
    /// ÉCRIT EN DUR, ET C'EST DÉLIBÉRÉ.
    ///
    /// Le calculer avec `KafkaEventNaming.EventType(typeof(...))` ferait passer le
    /// test quoi qu'il arrive : si la convention de nommage changeait, le
    /// producteur du test changerait avec elle, et le test continuerait de réussir
    /// pendant que les services déjà déployés cesseraient de se comprendre. Un nom
    /// de contrat s'écrit à la main dans un test, précisément pour qu'il faille
    /// venir le modifier ici.
    /// </summary>
    private const string TypeEvenement = "seller.closed";

    private const string NomConsumer = "catalog-service.merchants-seller-closed";

    private readonly CatalogIntegrationFixture _fixture;

    public InboxKafkaTests(CatalogIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Un événement publié sur le sujet est consommé, et la trace est écrite.
    /// </summary>
    [Fact]
    public async Task Un_evenement_publie_est_consomme_et_trace_dans_l_inbox()
    {
        // Le client force la construction de l'hôte, donc le démarrage du
        // consommateur d'arrière-plan. Sans lui, on publierait dans le vide.
        _ = _fixture.CreateClient();

        var eventId = Guid.NewGuid();

        await PublierAsync(eventId, sellerId: Guid.NewGuid());

        var traces = await AttendreTracesAsync(eventId, attendu: 1);

        traces.Should().Be(1,
            "le gestionnaire doit avoir traité l'événement et écrit sa trace dans la même "
            + "unité de travail");
    }

    /// <summary>
    /// LE MÊME ÉVÉNEMENT DEUX FOIS NE DOIT PRODUIRE QU'UNE TRACE.
    ///
    /// Kafka livre AU MOINS une fois : un rééquilibrage de partitions, un
    /// redémarrage, un offset non committé, et le message revient. C'est le cas
    /// nominal, pas l'incident.
    ///
    /// Ici les deux gestionnaires du catalogue sont naturellement idempotents — la
    /// garde `Status == Published` fait que la seconde passe ne dépublie rien —
    /// donc un rejeu ne se verrait PAS dans les produits. Ce test n'observe pas
    /// l'effet métier : il observe la GARDE, par la ligne qu'elle écrit. C'est le
    /// seul endroit où l'on peut affirmer qu'elle fonctionne avant qu'un
    /// gestionnaire non idempotent — un crédit de portefeuille, un décompte de
    /// stock — n'en dépende vraiment.
    /// </summary>
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
        //
        // Assérer immédiatement passerait même si l'inbox ne servait à rien : le
        // second message n'aurait simplement pas encore été consommé. On attend
        // donc que le consommateur ait eu le temps de le traiter, PUIS on vérifie
        // qu'il n'a rien ajouté.
        await Task.Delay(TimeSpan.FromSeconds(3));

        var traces = await CompterTracesAsync(eventId);

        traces.Should().Be(1,
            "la clé de l'inbox est le couple (événement, consumer) : un rejeu doit être ignoré");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Publie un message dans l'enveloppe que le consommateur attend.
    /// </summary>
    /// <remarks>
    /// `KafkaEventEnvelope` ET NON `HbaEventEnvelope`.
    ///
    /// Les deux coexistent le temps de la migration (voir l'encadré de
    /// `HbaEventEnvelope`) : les événements portant `[HbaEvent]` partent dans la
    /// seconde, les autres restent sur la première. `KafkaIntegrationEventConsumer`
    /// désérialise la PREMIÈRE. Se tromper d'enveloppe ici ne lèverait pas — le
    /// désérialiseur rendrait un objet aux champs vides, `eventType` serait nul, et
    /// le message serait ignoré en silence. Le test échouerait sur un compteur à
    /// zéro, sans dire pourquoi.
    ///
    /// `Id` DOIT ÊTRE DANS `Data`, PAS SEULEMENT DANS `EventId`.
    ///
    /// C'est `IntegrationEvent.Id` — donc une propriété de la charge utile — que le
    /// gestionnaire passe à l'inbox. Le `EventId` de l'enveloppe ne le renseigne
    /// pas : sans `id` dans `Data`, chaque rejeu recevrait un identifiant neuf à la
    /// désérialisation et l'inbox ne reconnaîtrait jamais rien.
    /// </remarks>
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

    /// <summary>
    /// Attend que la trace apparaisse, ou rend le compte atteint.
    /// </summary>
    /// <remarks>
    /// UNE ATTENTE ACTIVE, PAS UN `Task.Delay` FIXE.
    ///
    /// Le temps de consommation dépend de la machine, du démarrage du courtier et
    /// du rééquilibrage initial du groupe — qui peut prendre plusieurs secondes.
    /// Un délai fixe assez court rend le test instable ; assez long, il ralentit
    /// toute la suite. On sonde, et l'on s'arrête dès que c'est bon.
    ///
    /// Un test instable est pire qu'un test absent : on finit par le désactiver, et
    /// par désactiver ses voisins avec lui.
    /// </remarks>
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
