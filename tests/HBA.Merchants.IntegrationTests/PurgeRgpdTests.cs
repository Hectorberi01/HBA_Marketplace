using FluentAssertions;
using Npgsql;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>LE CONSOMMATEUR RGPD DU LOT 4, ÉPROUVÉ CONTRE UN VRAI COURTIER.</summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class PurgeRgpdTests
{
    /// <summary>Nom de ce consumer dans `consumer_inbox`.</summary>
    private const string NomConsumer = "seller-service.identity-user-anonymized";

    /// <summary>
    /// Nom d'événement tel que `KafkaEventNaming.EventType` le calcule :
    /// `UserAnonymizedIntegrationEvent` → suffixe retiré → `user.anonymized`.
    /// </summary>
    private const string TypeAnonymisation = "user.anonymized";

    private const string TypeEffacementPiece = "kyb.document.removed";

    private readonly MerchantsIntegrationFixture _fixture;

    public PurgeRgpdTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Le compte anonymisé voit son dossier fermé et chacune de ses pièces nommée.
    /// </summary>
    [Fact]
    public async Task Un_compte_anonymise_ferme_le_dossier_et_nomme_chaque_piece()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Purge {Guid.NewGuid():N}");
        await Parcours.DeposerPieceAsync(_fixture, vendeur, "IdCard");
        await Parcours.DeposerPieceAsync(_fixture, vendeur, "BusinessRegistry");

        var eventId = Guid.NewGuid();
        await AnonymiserAsync(eventId, vendeur.UserId);

        (await AttendreTracesAsync(eventId, attendu: 1)).Should().Be(1,
            "le gestionnaire doit avoir traité l'événement et écrit sa trace dans la "
            + "MÊME unité de travail que la purge — sans quoi un crash entre les deux "
            + "laisserait un effacement à moitié fait, indiscernable d'un rejeu");

        (await LireStatutAsync(vendeur.SellerId)).Should().Be("Closed",
            "on ferme avant de purger : purger les pièces d'un vendeur dont le catalogue "
            + "est encore en ligne laisserait une boutique tenue par un dossier vide");

        var effacements = await BusDeTest.AttendreAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetMerchant,
            e => e.EventType == TypeEffacementPiece
                 && e.AggregateId == vendeur.SellerId.ToString("D"),
            attendu: 2);

        effacements.Should().HaveCount(2,
            "une pièce, un événement : si l'effacement de l'une échoue durablement, les "
            + "autres partent quand même et le message en souffrance nomme le fichier qui résiste");
    }

    /// <summary>LE MÊME ÉVÉNEMENT DEUX FOIS NE DOIT RIEN RÉÉMETTRE.</summary>
    [Fact]
    public async Task Le_meme_evenement_rejoue_ne_reemet_aucun_ordre_d_effacement()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Rejeu {Guid.NewGuid():N}");
        await Parcours.DeposerPieceAsync(_fixture, vendeur, "IdCard");
        await Parcours.DeposerPieceAsync(_fixture, vendeur, "TaxId");

        var eventId = Guid.NewGuid();

        await AnonymiserAsync(eventId, vendeur.UserId);
        (await AttendreTracesAsync(eventId, attendu: 1)).Should().Be(1);

        var apresPremierPassage = await BusDeTest.AttendreAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetMerchant,
            e => e.EventType == TypeEffacementPiece
                 && e.AggregateId == vendeur.SellerId.ToString("D"),
            attendu: 2);

        apresPremierPassage.Should().HaveCount(2);

        // Même identifiant d'événement : c'est exactement ce que fait un rejeu.
        await AnonymiserAsync(eventId, vendeur.UserId);

        // ON LAISSE VOLONTAIREMENT LE TEMPS AU DÉFAUT DE SE PRODUIRE.
        await Task.Delay(TimeSpan.FromSeconds(20));

        (await CompterTracesAsync(eventId)).Should().Be(1,
            "la clé de l'inbox est le couple (événement, consumer) : un rejeu doit être ignoré");

        var apresRejeu = BusDeTest.Drainer(_fixture.BootstrapServers, BusDeTest.SujetMerchant)
            .Count(e => e.EventType == TypeEffacementPiece
                        && e.AggregateId == vendeur.SellerId.ToString("D"));

        apresRejeu.Should().Be(2,
            "sans la garde d'inbox, `MarkForDeletion` aurait renommé les deux mêmes "
            + "pièces et media-service aurait reçu deux ordres d'effacement pour des "
            + "fichiers déjà supprimés");
    }

    // Outillage

    private Task AnonymiserAsync(Guid eventId, Guid userId)
        => BusDeTest.PublierAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetIdentity,
            eventId,
            TypeAnonymisation,
            aggregateType: "user",
            aggregateId: userId.ToString("D"),
            charge: new
            {
                // `id`, ET PAS SEULEMENT `eventId` DANS L'ENVELOPPE. C'est
                // `IntegrationEvent.Id` que le gestionnaire passe à l'inbox.
                id = eventId,
                occurredOnUtc = DateTime.UtcNow,
                userId
            });

    /// <summary>Attend que la trace apparaisse, ou rend le compte atteint.</summary>
    private async Task<int> AttendreTracesAsync(Guid eventId, int attendu)
    {
        var echeance = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < echeance)
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
            FROM sellers.consumer_inbox
            WHERE "EventId" = @eventId AND "ConsumerName" = @consumer
            """,
            connexion);

        commande.Parameters.AddWithValue("eventId", eventId);
        commande.Parameters.AddWithValue("consumer", NomConsumer);

        return Convert.ToInt32(await commande.ExecuteScalarAsync());
    }

    private async Task<string> LireStatutAsync(Guid sellerId)
    {
        await using var connexion = new NpgsqlConnection(_fixture.ConnectionString);
        await connexion.OpenAsync();

        await using var commande = new NpgsqlCommand(
            """
            SELECT "Status" FROM sellers.sellers WHERE "Id" = @id
            """,
            connexion);

        commande.Parameters.AddWithValue("id", sellerId);

        return (string)(await commande.ExecuteScalarAsync())!;
    }
}
