using FluentAssertions;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ISSUE-002 — « L'ACHETEUR EST DÉBITÉ ; LA COMMANDE RESTE `AwaitingPayment`
/// INDÉFINIMENT. ARGENT ENCAISSÉ SANS CONTREPARTIE. »
/// </summary>
[Collection(OrderIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class ConfirmationApresPaiementTests
{
    /// <summary>
    /// Le nom d'événement tel que `KafkaEventNaming.EventType` le calcule :
    /// `PaymentCapturedIntegrationEvent` → suffixe `IntegrationEvent` retiré →
    /// PascalCase séparé par des points en minuscules → `payment.captured`.
    /// </summary>
    private const string TypeCapture = "payment.captured";

    /// <summary>
    /// Le nom sous lequel ce gestionnaire est inscrit dans
    /// `ordering.consumer_inbox`.
    /// </summary>
    private const string ConsommateurCapture =
        "HBA.Orders.Application.Orders.EventHandlers.ConfirmOrderOnPaymentCapturedHandler";

    /// <summary>`OrderConfirmedIntegrationEvent` → `order.confirmed`.</summary>
    private const string TypeConfirmation = "order.confirmed";

    private readonly OrderIntegrationFixture _fixture;

    public ConfirmationApresPaiementTests(OrderIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>LA CHAÎNE D'ISSUE-002, DE BOUT EN BOUT ET POUR DE VRAI.</summary>
    [Fact]
    public async Task Un_paiement_capture_fait_passer_la_commande_en_payee()
    {
        var commande = await Parcours.PasserCommandeAsync(_fixture);

        var avantPaiement = await BaseDeTest.LireCommandeAsync(
            _fixture.ConnectionString, commande.CommandeId);

        avantPaiement.Should().NotBeNull(
            "le POST /api/orders doit avoir écrit la commande : sans elle, le paiement "
            + "capturé n'aurait rien à confirmer et le test ne prouverait rien");

        avantPaiement!.Statut.Should().Be("AwaitingPayment",
            "c'est l'état d'où part ISSUE-002 : le stock est réservé et l'on attend "
            + "le prestataire de paiement");

        var eventId = Guid.NewGuid();
        var paiementId = Guid.NewGuid();

        await PublierCaptureAsync(eventId, paiementId, commande.CommandeId);

        var etat = await BaseDeTest.AttendreStatutAsync(
            _fixture.ConnectionString, commande.CommandeId, "Confirmed");

        // « Confirmed » ET NON « Paid », ET CE N'EST PAS UN RACCOURCI.
        etat.Should().NotBeNull();
        etat!.Statut.Should().Be("Confirmed",
            "le paiement capturé doit traverser le courtier, être reconnu par le "
            + "consommateur, atteindre ConfirmOrderOnPaymentCapturedHandler et changer "
            + "l'état de la commande — c'est la chaîne entière d'ISSUE-002");

        etat.PaiementId.Should().Be(paiementId,
            "la commande doit porter le paiement qui l'a soldée : c'est ce qui rend un "
            + "remboursement possible, et c'est aussi la preuve que la migration "
            + "AddOrderPaymentId s'applique — elle était restée INERTE, faute des "
            + "attributs [DbContext] et [Migration], et la colonne n'existait dans aucune base");

        (await BaseDeTest.AttendreTracesAsync(
            _fixture.ConnectionString, eventId, ConsommateurCapture, attendu: 1))
            .Should().Be(1,
                "le dispatcher doit avoir posé la trace d'inbox dans la MÊME unité de travail "
                + "que la confirmation — sans quoi un incident entre les deux laisserait une "
                + "commande confirmée sans trace, indiscernable d'un message jamais reçu");

        // LE STOCK EST SOLDÉ LIGNE PAR LIGNE, PAS GLOBALEMENT.
        var soldes = _fixture.Inventaire.Pour("confirm", commande.CommandeId);

        soldes.Select(g => g.Sku).Should().BeEquivalentTo(commande.Skus,
            "la confirmation solde la réservation de CHAQUE ligne de marchandise");

        soldes.Should().OnlyContain(g => g.LieuId == commande.LieuExpedition,
            "une réservation se solde là où elle a été posée : le SKU seul ne la désigne pas");

        // SANS COURSE, AUCUN VENDEUR N'EST JAMAIS RÉGLÉ.
        var reference = $"ORDER-{commande.CommandeId:N}";

        var courseDemandee = await AttendreAsync(
            () => _fixture.Courses.CoursesDemandees.Contains(reference));

        courseDemandee.Should().BeTrue(
            "la confirmation doit déclencher la demande de course sous la référence "
            + $"« {reference} » — le format « ORDER-<guid:N> » est un contrat avec "
            + "delivery-service, qui rend la référence telle quelle dans ses événements");
    }

    /// <summary>LE MÊME ÉVÉNEMENT DEUX FOIS NE DOIT PRODUIRE QU'UN SEUL EFFET.</summary>
    [Fact]
    public async Task Le_meme_evenement_rejoue_ne_confirme_qu_une_fois()
    {
        var commande = await Parcours.PasserCommandeAsync(_fixture);

        var eventId = Guid.NewGuid();
        var paiementId = Guid.NewGuid();

        await PublierCaptureAsync(eventId, paiementId, commande.CommandeId);

        var apresCapture = await BaseDeTest.AttendreStatutAsync(
            _fixture.ConnectionString, commande.CommandeId, "Confirmed");

        // ON VÉRIFIE LE PREMIER PASSAGE AVANT DE PARLER DE REJEU.
        apresCapture.Should().NotBeNull();
        apresCapture!.Statut.Should().Be("Confirmed",
            "le rejeu ne prouve rien tant que le premier passage n'a pas produit son effet");

        var apresPremierPassage = await BusDeTest.AttendreAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetOrder,
            e => e.EventType == TypeConfirmation
                 && e.AggregateId == commande.CommandeId.ToString("D"),
            attendu: 1);

        apresPremierPassage.Should().HaveCount(1);

        // Même identifiant d'événement dans `data.id` : c'est exactement ce que
        // fait un rejeu.
        await PublierCaptureAsync(eventId, paiementId, commande.CommandeId);

        // ON LAISSE VOLONTAIREMENT LE TEMPS AU DÉFAUT DE SE PRODUIRE.
        await Task.Delay(TimeSpan.FromSeconds(20));

        (await BaseDeTest.CompterTracesAsync(
            _fixture.ConnectionString, eventId, ConsommateurCapture))
            .Should().Be(1,
                "la clé de l'inbox est le couple (événement, consommateur) : un rejeu doit "
                + "être reconnu et le gestionnaire ne doit pas être appelé du tout");

        var apresRejeu = BusDeTest.Drainer(_fixture.BootstrapServers, BusDeTest.SujetOrder)
            .Count(e => e.EventType == TypeConfirmation
                        && e.AggregateId == commande.CommandeId.ToString("D"));

        apresRejeu.Should().Be(1,
            "une seconde confirmation republierait `order.confirmed`, et son consommateur "
            + "— CreateDeliveryOnOrderConfirmedHandler, qui n'a AUCUNE garde d'état — "
            + "commanderait un SECOND LIVREUR pour le même colis : deux devis de course "
            + "facturés, la commande close à la première remise, et une course orpheline");

        _fixture.Inventaire.Pour("confirm", commande.CommandeId)
            .Should().HaveCount(commande.Skus.Count,
                "le stock ne doit être soldé qu'une fois par ligne : le décrémenter deux fois "
                + "ferait disparaître de la marchandise qui n'a été vendue qu'une seule fois");

        var apresTout = await BaseDeTest.LireCommandeAsync(
            _fixture.ConnectionString, commande.CommandeId);

        apresTout.Should().NotBeNull();
        apresTout!.Statut.Should().Be("Confirmed",
            "le rejeu ne doit rien changer — ni avancer, ni faire reculer la commande");
    }

    // Outillage

    private Task PublierCaptureAsync(Guid eventId, Guid paiementId, Guid commandeId)
        => BusDeTest.PublierAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetFinancial,
            eventId,
            TypeCapture,

            // `KafkaEventNaming.AggregateType("payment.captured")` = le segment
            // avant le premier point.
            aggregateType: "payment",
            aggregateId: commandeId.ToString("D"),
            charge: new
            {
                // `id`, ET PAS SEULEMENT `eventId` DANS L'ENVELOPPE. C'est
                // `IntegrationEvent.Id` que le dispatcher passe à l'inbox.
                id = eventId,
                occurredOnUtc = DateTime.UtcNow,
                paymentId = paiementId,
                orderId = commandeId,

                // OBLIGATOIRE : la propriété est `required` sur le contrat, et
                // System.Text.Json REFUSE de désérialiser sans elle.
                orderType = "MARKETPLACE"
            });

    /// <summary>Attend qu'une condition observée dans un double devienne vraie.</summary>
    private static async Task<bool> AttendreAsync(Func<bool> condition)
    {
        var echeance = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < echeance)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return condition();
    }
}
