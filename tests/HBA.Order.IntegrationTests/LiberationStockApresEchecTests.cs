using FluentAssertions;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-003 — « LA COMMANDE ÉCHOUE, LE STOCK RESTE RÉSERVÉ SANS LIMITE DE TEMPS.
/// SURVENTE PAR ÉTRANGLEMENT : CUMULATIF, CHAQUE PAIEMENT ÉCHOUÉ EN RETIRE UN PEU
/// PLUS. »
///
/// C'EST LA PANNE LA MOINS SPECTACULAIRE ET LA PLUS DIFFICILE À VOIR.
///
/// Un débit sans commande se remarque : le client réclame. Une réservation
/// jamais libérée, non — l'article cesse simplement d'être vendable, sans que
/// rien ne l'explique. Le vendeur voit son stock « disponible » descendre alors
/// que sa réserve physique est pleine, et personne ne relie cela à des paiements
/// refusés des semaines plus tôt. Sur un moyen de paiement mobile où l'échec est
/// ordinaire, le disponible s'étrangle commande après commande.
///
/// ET C'EST UN APPEL SORTANT, DONC INVISIBLE DANS LE SCHÉMA `ordering`.
///
/// La libération est un appel à inventory-service. Constater en base que la
/// commande passe à « Cancelled » ne prouve rien : une commande annulée dont les
/// réservations restent posées est EXACTEMENT l'état de la panne. Le seul
/// observable qui distingue les deux est la liste des appels reçus par
/// `IInventoryModuleApi` — d'où le double qui enregistre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(OrderIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class LiberationStockApresEchecTests
{
    /// <summary>
    /// `PaymentFailedIntegrationEvent` → suffixe `IntegrationEvent` retiré →
    /// `payment.failed`.
    /// </summary>
    /// <remarks>
    /// ÉCRIT À LA MAIN, comme le sujet et comme le nom de consommateur, et pour
    /// la même raison : calculer le nom avec `KafkaEventNaming.EventType` ferait
    /// dériver le test AVEC le producteur, en silence, pendant que les messages
    /// déjà en rétention et les autres services resteraient sur l'ancien nom. Un
    /// nom d'événement est un contrat, et un contrat se relit.
    /// </remarks>
    private const string TypeEchec = "payment.failed";

    /// <summary>
    /// Le nom sous lequel ce gestionnaire est inscrit dans `ordering.consumer_inbox` :
    /// le `FullName` du type, tel que `IntegrationEventDispatcher` le calcule.
    /// Stable, parce qu'il est EN BASE — renommer la classe rendrait « jamais
    /// traités » tous les événements de l'historique.
    /// </summary>
    private const string ConsommateurEchec =
        "HBA.Orders.Application.Orders.EventHandlers.CancelOrderOnPaymentFailedHandler";

    private readonly OrderIntegrationFixture _fixture;

    public LiberationStockApresEchecTests(OrderIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMPENSATION D'ISSUE-003, ÉPROUVÉE LIGNE PAR LIGNE.
    ///
    /// « UNE LIBÉRATION PAR LIGNE » N'EST PAS LA MÊME EXIGENCE QUE « UNE
    ///    LIBÉRATION ».
    ///
    /// La commande porte deux SKU. Un service qui ne libérerait que la première
    /// ligne passerait un test qui compte « au moins un appel » — et étranglerait
    /// tout de même le disponible du second article, à chaque paiement échoué. La
    /// survente par étranglement est cumulative : c'est la ligne oubliée qui la
    /// produit, pas la commande oubliée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task Un_paiement_echoue_libere_la_reservation_de_stock()
    {
        var commande = await Parcours.PasserCommandeAsync(_fixture);

        // Le parcours a bien POSÉ des réservations : sans elles, il n'y aurait
        // rien à libérer et l'assertion suivante serait vide de sens.
        _fixture.Inventaire.Pour("reserve", commande.CommandeId)
            .Select(g => g.Sku)
            .Should().BeEquivalentTo(commande.Skus,
                "le checkout réserve le stock de chaque ligne de marchandise — c'est cette "
                + "réservation-là qui reste posée pour toujours quand le paiement échoue");

        var eventId = Guid.NewGuid();
        const string motif = "Solde insuffisant sur le compte mobile.";

        await PublierEchecAsync(eventId, commande.CommandeId, motif);

        var etat = await BaseDeTest.AttendreStatutAsync(
            _fixture.ConnectionString, commande.CommandeId, "Cancelled");

        etat.Should().NotBeNull();
        etat!.Statut.Should().Be("Cancelled",
            "le paiement échoué doit traverser le courtier, être reconnu par le "
            + "consommateur et atteindre CancelOrderOnPaymentFailedHandler — c'est la "
            + "chaîne entière d'ISSUE-003");

        etat.MotifAnnulation.Should().Contain(motif,
            "le motif du prestataire doit voyager jusqu'à la commande : sans lui, "
            + "l'acheteur voit une commande annulée sans savoir qu'il peut simplement "
            + "recharger son compte et recommencer");

        // ═════════════════════════════════════════════════════════════════════
        // VOICI LA PREUVE. Le reste n'était que le chemin pour y arriver.
        // ═════════════════════════════════════════════════════════════════════
        var liberations = _fixture.Inventaire.Pour("release", commande.CommandeId);

        liberations.Select(g => g.Sku).Should().BeEquivalentTo(commande.Skus,
            "chaque ligne qui a réservé du stock doit le rendre. Une ligne oubliée, "
            + "c'est un article qui cesse d'être vendable sans que rien ne l'explique — "
            + "et l'étranglement est cumulatif, paiement échoué après paiement échoué");

        liberations.Should().OnlyContain(g => g.LieuId == commande.LieuExpedition,
            "une réservation se libère là où elle a été posée : inventory-service indexe "
            + "par (SKU, lieu, commande), et un lieu erroné rendrait du stock ailleurs — "
            + "donc en créerait à un endroit et le laisserait bloqué à l'autre");

        // AUCUN SOLDE DE RÉSERVATION : le paiement a ÉCHOUÉ.
        //
        // `ConfirmReservationAsync` décrémente le stock PHYSIQUE. L'appeler ici
        // ferait disparaître de la marchandise pour une vente qui n'a jamais eu
        // lieu — l'erreur exactement inverse, et bien plus difficile à défaire.
        _fixture.Inventaire.Pour("confirm", commande.CommandeId).Should().BeEmpty(
            "un paiement échoué ne solde rien : la marchandise n'a pas été vendue");

        (await BaseDeTest.AttendreTracesAsync(
            _fixture.ConnectionString, eventId, ConsommateurEchec, attendu: 1))
            .Should().Be(1,
                "la trace d'inbox part dans la MÊME unité de travail que l'annulation : "
                + "c'est ce qui rend le rejeu de ce message inoffensif");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    private Task PublierEchecAsync(Guid eventId, Guid commandeId, string motif)
        => BusDeTest.PublierAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetFinancial,
            eventId,
            TypeEchec,
            aggregateType: "payment",
            aggregateId: commandeId.ToString("D"),
            charge: new
            {
                // `id`, ET PAS SEULEMENT `eventId` DANS L'ENVELOPPE : c'est
                // `IntegrationEvent.Id` que le dispatcher passe à l'inbox.
                id = eventId,
                occurredOnUtc = DateTime.UtcNow,
                paymentId = Guid.NewGuid(),
                orderId = commandeId,

                // Les deux propriétés sont `required` sur le contrat :
                // System.Text.Json REFUSE de désérialiser sans elles, et
                // l'événement serait perdu sur une commande restée immobile —
                // l'image exacte d'ISSUE-003, pour une raison qui n'a rien à voir.
                orderType = "MARKETPLACE",
                reason = motif
            });
}
