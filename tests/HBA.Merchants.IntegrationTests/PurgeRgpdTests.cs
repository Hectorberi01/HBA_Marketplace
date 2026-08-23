using FluentAssertions;
using Npgsql;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CONSOMMATEUR RGPD DU LOT 4, ÉPROUVÉ CONTRE UN VRAI COURTIER.
///
/// C'EST LE SEUL CONSOMMATEUR DU DÉPÔT SANS IDEMPOTENCE NATURELLE.
///
/// Partout ailleurs, la garde d'inbox du §19.5 est une ceinture de sécurité qu'on
/// ne peut pas observer : les gestionnaires du catalogue sont naturellement
/// idempotents — `Status == Published` fait que la seconde passe ne dépublie rien
/// — donc un rejeu ne se VOIT pas dans les données. `InboxKafkaTests` doit s'y
/// rabattre sur l'observation de la ligne écrite.
///
/// Ici, non. `Seller.MarkForDeletion` réémet UN ÉVÉNEMENT D'EFFACEMENT PAR PIÈCE à
/// chaque passage : un simple rééquilibrage de partitions ferait redemander à
/// media-service la suppression de fichiers déjà supprimés. Bruit, lettres mortes,
/// et un journal qui raconte un effacement qui n'a pas eu lieu.
///
/// La garde est donc load-bearing, elle n'avait jamais été éprouvée, et c'est le
/// second des trois parcours que le plan réclamait.
///
/// ET C'EST AUSSI LE SEUL ENDROIT QUI VÉRIFIE LE DROIT À L'EFFACEMENT.
///
/// `kyb_documents` pointe vers des cartes d'identité, des registres de commerce et
/// des documents fiscaux. Avant le lot 4, seller-service n'écoutait pas
/// `identity.user.anonymized` : les pièces survivaient au compte, sans plus rien
/// pour les relier à une personne — c'est-à-dire dans l'état exact où plus
/// personne ne peut les retrouver pour les effacer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class PurgeRgpdTests
{
    /// <summary>
    /// Nom de ce consumer dans `consumer_inbox`. Écrit en dur, comme la clé qu'il est.
    /// </summary>
    /// <remarks>
    /// LE LIRE DEPUIS LA CONSTANTE DU GESTIONNAIRE FERAIT PASSER LE TEST QUOI
    /// QU'IL ARRIVE.
    ///
    /// Cette chaîne est EN BASE, sur toutes les lignes déjà écrites. La changer
    /// ferait rejouer à seller-service tous les effacements de l'historique — donc
    /// redemander à media-service la suppression de fichiers disparus depuis des
    /// mois. Un nom de contrat s'écrit à la main dans un test, précisément pour
    /// qu'il faille venir le modifier ici.
    /// </remarks>
    private const string NomConsumer = "seller-service.identity-user-anonymized";

    /// <summary>
    /// Nom d'événement tel que `KafkaEventNaming.EventType` le calcule :
    /// `UserAnonymizedIntegrationEvent` → suffixe retiré → `user.anonymized`.
    /// En dur pour la même raison.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE MÊME ÉVÉNEMENT DEUX FOIS NE DOIT RIEN RÉÉMETTRE.
    ///
    /// Kafka livre AU MOINS une fois : un rééquilibrage de partitions, un
    /// redémarrage, un offset non committé, et le message revient. C'est le cas
    /// nominal, pas l'incident.
    ///
    /// Sans la garde, la seconde passe rejouerait `MarkForDeletion` et renverrait
    /// deux ordres d'effacement pour des fichiers déjà supprimés. Ce test est le
    /// seul du dépôt où l'absence de garde se traduirait par un effet MÉTIER
    /// observable, et non par une simple ligne de trace en double.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
        //
        // Assérer immédiatement passerait même si l'inbox ne servait à rien : le
        // second message n'aurait simplement pas encore été consommé, et l'outbox
        // ne scrute que toutes les cinq secondes. On attend donc que le
        // consommateur ET le processeur aient eu le temps de faire le mal, PUIS on
        // vérifie qu'ils ne l'ont pas fait.
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

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

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
                // `id`, ET PAS SEULEMENT `eventId` DANS L'ENVELOPPE.
                // C'est `IntegrationEvent.Id` que le gestionnaire passe à l'inbox.
                id = eventId,
                occurredOnUtc = DateTime.UtcNow,
                userId
            });

    /// <summary>
    /// Attend que la trace apparaisse, ou rend le compte atteint.
    /// </summary>
    /// <remarks>
    /// UNE ATTENTE ACTIVE, PAS UN `Task.Delay` FIXE. Le temps de consommation
    /// dépend de la machine et du rééquilibrage initial du groupe, qui peut prendre
    /// plusieurs secondes. Un délai fixe assez court rend le test instable ; assez
    /// long, il ralentit toute la suite.
    /// </remarks>
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
