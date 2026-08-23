using FluentAssertions;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-002 — « L'ACHETEUR EST DÉBITÉ ; LA COMMANDE RESTE `AwaitingPayment`
/// INDÉFINIMENT. ARGENT ENCAISSÉ SANS CONTREPARTIE. »
///
/// CE N'EST PAS LE GESTIONNAIRE QUI MANQUAIT — C'EST LE MESSAGE.
///
/// `ConfirmOrderOnPaymentCapturedHandler` existe, il est enregistré
/// (`OrderingModuleInstaller`), et un test unitaire le montrerait parfaitement
/// fonctionnel. Il ne recevait simplement RIEN : payment-service publiait sur
/// `service.payment.v1` — le `SERVICE_NAME` amputé de « -service » — pendant
/// qu'order-service écoutait `service.financial.v1`, le domaine. Un message part,
/// il est acquitté, il n'arrive nulle part. Aucune erreur, aucun avertissement.
///
/// C'est pourquoi ce test PUBLIE SUR LE COURTIER au lieu d'appeler le
/// gestionnaire : ce qui était cassé se trouve entre les deux, et nulle part
/// ailleurs. Appeler `HandleAsync` directement passerait au vert sur une
/// plateforme qui, déployée, encaisserait sans jamais confirmer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(OrderIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class ConfirmationApresPaiementTests
{
    /// <summary>
    /// Le nom d'événement tel que `KafkaEventNaming.EventType` le calcule :
    /// `PaymentCapturedIntegrationEvent` → suffixe `IntegrationEvent` retiré →
    /// PascalCase séparé par des points en minuscules → `payment.captured`.
    /// </summary>
    /// <remarks>
    /// ÉCRIT À LA MAIN, ET NON OBTENU EN APPELANT LA FONCTION.
    ///
    /// L'appeler ferait passer ce test quoi qu'elle calcule : le producteur et le
    /// test dériveraient le nom de la même source, et un changement de convention
    /// les déplacerait ensemble, en silence, pendant que les messages déjà en
    /// rétention et les consommateurs des autres services resteraient sur l'ancien
    /// nom. Un nom d'événement est un CONTRAT ; il s'écrit à la main dans un test,
    /// précisément pour qu'il faille venir le modifier ici.
    ///
    /// ET CE N'EST PAS `payment.succeeded`, MALGRÉ `[HbaEvent]`.
    ///
    /// La classe porte `[HbaEvent("payment.succeeded")]`, qui est le nom du §10.12.
    /// Mais `HbaEventNaming` n'est PAS ENCORE BRANCHÉ (voir son en-tête : « écrit
    /// pour cela et pas encore branché ») : le producteur comme le consommateur
    /// passent tous deux par `KafkaEventNaming`. Écrire ici le nom canonique
    /// donnerait un test qui n'éprouve rien — l'événement ne serait reconnu par
    /// personne. Le jour où la bascule aura lieu, cette ligne devra changer, et
    /// c'est bien qu'elle soit à cet endroit.
    /// </remarks>
    private const string TypeCapture = "payment.captured";

    /// <summary>
    /// Le nom sous lequel ce gestionnaire est inscrit dans `ordering.consumer_inbox`.
    /// </summary>
    /// <remarks>
    /// C'EST LE NOM COMPLET DU TYPE, ET IL EST EN BASE.
    ///
    /// `IntegrationEventDispatcher.NomDuConsommateur` prend le `FullName` du
    /// gestionnaire — espace de noms compris, parce que deux modules composés dans
    /// le même hôte peuvent avoir un `PaymentCapturedHandler` chacun et ne doivent
    /// pas se faire taire l'un l'autre.
    ///
    /// Le lire depuis la classe (`typeof(...).FullName`) ferait passer ce test
    /// quoi qu'il arrive. Or renommer le gestionnaire rendrait « jamais traités »
    /// tous les événements de l'historique : au prochain rejeu, ils referaient
    /// leur effet. Un renommage de handler est une migration, pas du confort
    /// d'IDE — d'où le littéral.
    /// </remarks>
    private const string ConsommateurCapture =
        "HBA.Orders.Application.Orders.EventHandlers.ConfirmOrderOnPaymentCapturedHandler";

    /// <summary>`OrderConfirmedIntegrationEvent` → `order.confirmed`. Même règle.</summary>
    private const string TypeConfirmation = "order.confirmed";

    private readonly OrderIntegrationFixture _fixture;

    public ConfirmationApresPaiementTests(OrderIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA CHAÎNE D'ISSUE-002, DE BOUT EN BOUT ET POUR DE VRAI.
    ///
    /// CE QUE CE TEST EMPÊCHE DE REVENIR : L'ARGENT ENCAISSÉ SANS CONTREPARTIE.
    ///
    /// Une commande qui reste `AwaitingPayment` après un débit n'est pas une
    /// anomalie technique : c'est un client qui a payé et qui n'a rien. Le stock
    /// n'est pas décrémenté, la course n'est jamais demandée, le vendeur n'est
    /// jamais réglé, et RIEN dans les journaux ne relie le débit à l'immobilité —
    /// puisque, du point de vue de payment-service, le message est parti et a été
    /// acquitté.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
        //
        // `ConfirmOrderPaymentCommandHandler` enchaîne `MarkPaid`, le solde des
        // réservations, puis `Confirm()` — le tout dans UNE seule unité de
        // travail. `Paid` est un état intermédiaire en mémoire, jamais committé
        // seul. Attendre « Paid » en base attendrait donc pour toujours.
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
        //
        // Une ligne oubliée ici, c'est de la marchandise vendue et jamais
        // décrémentée : la réservation reste posée pour toujours sur un article
        // pourtant parti.
        var soldes = _fixture.Inventaire.Pour("confirm", commande.CommandeId);

        soldes.Select(g => g.Sku).Should().BeEquivalentTo(commande.Skus,
            "la confirmation solde la réservation de CHAQUE ligne de marchandise");

        soldes.Should().OnlyContain(g => g.LieuId == commande.LieuExpedition,
            "une réservation se solde là où elle a été posée : le SKU seul ne la désigne pas");

        // SANS COURSE, AUCUN VENDEUR N'EST JAMAIS RÉGLÉ.
        //
        // La commande confirmée publie `OrderConfirmed`, qu'order-service consomme
        // lui-même pour demander la course. Sans elle : pas de `DeliveryCompleted`,
        // donc jamais « livrée », donc escrow jamais libéré. C'est le maillon qui
        // manquait depuis que Shipping n'a pas été extrait du monolithe.
        var reference = $"ORDER-{commande.CommandeId:N}";

        var courseDemandee = await AttendreAsync(
            () => _fixture.Courses.CoursesDemandees.Contains(reference));

        courseDemandee.Should().BeTrue(
            "la confirmation doit déclencher la demande de course sous la référence "
            + $"« {reference} » — le format « ORDER-<guid:N> » est un contrat avec "
            + "delivery-service, qui rend la référence telle quelle dans ses événements");
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE MÊME ÉVÉNEMENT DEUX FOIS NE DOIT PRODUIRE QU'UN SEUL EFFET.
    ///
    /// Kafka livre AU MOINS une fois : un rééquilibrage de partitions, un
    /// redémarrage, un offset non committé, et le message revient. C'est le cas
    /// nominal, pas l'incident. L'audit l'exige explicitement — « rejeu du même
    /// événement → un seul effet » — et c'est la garde d'inbox du lot 2.1 qui le
    /// tient.
    ///
    /// CE QU'IL FAUT SAVOIR POUR NE PAS SE MENTIR SUR CE QUE CE TEST PROUVE.
    ///
    /// Ici, DEUX filets se superposent, et il faut les distinguer :
    ///
    ///   • l'INBOX — `IntegrationEventDispatcher` reconnaît le couple
    ///     (événement, consommateur) et n'appelle pas le gestionnaire du tout ;
    ///   • l'AGRÉGAT — même sans inbox, `MarkPaid()` exige `AwaitingPayment` et
    ///     refuserait une seconde confirmation. C'est l'« idempotence par
    ///     accident » que décrit `AjoutInboxConsommateur` : elle tient tant que la
    ///     transition reste interdite, et elle ne dit rien du gestionnaire qui n'a
    ///     pas de garde d'état — `CreateDeliveryOnOrderConfirmedHandler` n'en a
    ///     pas, et un `OrderConfirmed` rejoué lui ferait commander UN SECOND
    ///     LIVREUR pour le même colis.
    ///
    /// Ce test assertit donc les deux : l'unicité de la TRACE (ce que fait
    /// l'inbox, et elle seule) et l'unicité de l'EFFET publié. Le premier
    /// tomberait si la garde disparaissait ; le second est ce que l'audit demande
    /// de constater.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
        //
        // Sans cette assertion, une chaîne toujours rompue rendrait ce test VERT :
        // zéro confirmation vaut bien « pas plus d'une ». Le rejeu ne prouve
        // quelque chose que si l'effet a d'abord eu lieu.
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
        // fait un rejeu. Un identifiant neuf serait un autre paiement.
        await PublierCaptureAsync(eventId, paiementId, commande.CommandeId);

        // ON LAISSE VOLONTAIREMENT LE TEMPS AU DÉFAUT DE SE PRODUIRE.
        //
        // Assérer immédiatement passerait même si l'inbox ne servait à rien : le
        // second message n'aurait simplement pas encore été consommé, et le
        // processeur d'outbox ne scrute que toutes les cinq secondes. On attend
        // donc que le consommateur ET le processeur aient eu le temps de faire le
        // mal, PUIS on vérifie qu'ils ne l'ont pas fait.
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

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    private Task PublierCaptureAsync(Guid eventId, Guid paiementId, Guid commandeId)
        => BusDeTest.PublierAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetFinancial,
            eventId,
            TypeCapture,

            // `KafkaEventNaming.AggregateType("payment.captured")` = le segment
            // avant le premier point. Et l'agrégat retenu pour un événement de
            // paiement est l'`OrderId` — c'est ce qui garantit que capture et
            // échec d'une même commande restent ordonnés dans une partition.
            aggregateType: "payment",
            aggregateId: commandeId.ToString("D"),
            charge: new
            {
                // `id`, ET PAS SEULEMENT `eventId` DANS L'ENVELOPPE.
                // C'est `IntegrationEvent.Id` que le dispatcher passe à l'inbox.
                id = eventId,
                occurredOnUtc = DateTime.UtcNow,
                paymentId = paiementId,
                orderId = commandeId,

                // OBLIGATOIRE : la propriété est `required` sur le contrat, et
                // System.Text.Json REFUSE de désérialiser sans elle. Sans ce
                // champ, l'événement échouerait à la désérialisation et le test
                // constaterait une commande immobile — c'est-à-dire l'image exacte
                // d'ISSUE-002, pour une raison qui n'a rien à voir.
                orderType = "MARKETPLACE"
            });

    /// <summary>
    /// Attend qu'une condition observée dans un double devienne vraie.
    /// </summary>
    /// <remarks>
    /// ATTENTE ACTIVE, MÊME RAISON QUE PARTOUT AILLEURS ICI : l'effet arrive
    /// par le courtier puis par le processeur d'outbox, et sa durée dépend de la
    /// machine. Un `Task.Delay` fixe serait instable ou lent, au choix.
    /// </remarks>
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
