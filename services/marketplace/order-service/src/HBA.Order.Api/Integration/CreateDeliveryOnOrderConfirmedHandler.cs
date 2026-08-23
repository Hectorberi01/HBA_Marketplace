using HBA.Deliveries.Contracts;
using HBA.Inventory.Contracts;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Application.Orders.EventHandlers;
using HBA.Orders.Contracts;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;

namespace HBA.Orders.Api.Integration;

/// <summary>
/// Commande marketplace payée → une course est demandée.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// RIEN NE CRÉAIT DE COURSE POUR UN COLIS, ET C'EST CE QUI EMPÊCHAIT DE PAYER
///    LES VENDEURS.
///
/// Dans le monolithe, c'était Shipping qui posait la référence `SHIP-…` quand un
/// colis était déclaré prêt. Shipping n'a jamais été extrait. Depuis, la chaîne
/// s'arrêtait au paiement : aucune course, donc aucun `DeliveryCompleted`, donc
/// la commande n'atteignait jamais « livrée », donc l'escrow n'était jamais
/// libéré et **aucun vendeur n'était réglé**.
///
/// `MarkOrderDeliveredOnDeliveryCompletedHandler` attendait une référence
/// `ORDER-…` que personne ne posait. Ce gestionnaire la pose.
///
/// CE N'EST PAS UN ÉQUIVALENT DE SHIPPING.
///
/// Shipping gérait un colis par couple (vendeur, lieu d'expédition), avec
/// emballage, transporteur et numéro de suivi. Ici, une commande donne UNE
/// course, du lieu d'expédition à l'acheteur. C'est suffisant tant qu'une
/// commande part d'un seul endroit — et ce gestionnaire REFUSE explicitement
/// le reste plutôt que d'inventer.
///
/// PLUSIEURS LIEUX D'EXPÉDITION : ON NE CRÉE RIEN, ET LA COMMANDE PASSE EN
///    ARBITRAGE.
///
/// Créer une course par lieu serait pire que ne rien faire : la clôture de
/// commande se déclenche à la PREMIÈRE course terminée. Le vendeur du second
/// colis serait payé avant que son colis ne parte. Le refus est donc juste.
///
/// CE REFUS SE TERMINAIT PAR UN `return;` NU, ET C'ÉTAIT UNE IMPASSE.
///
/// Un journal en erreur, puis plus rien. La commande restait `Confirmed` POUR
/// TOUJOURS : ni livraison, ni annulation, ni remboursement, escrow gelé, stock
/// déjà décrémenté, argent encaissé — et un acheteur qui attend un colis que
/// personne n'apportera. Le journal disait « devra être traitée manuellement »
/// sans qu'aucune file ne le recueille : il fallait qu'un exploitant lise les
/// journaux au bon moment.
///
/// La commande entre désormais en ARBITRAGE. Elle sort du « en cours », elle
/// porte son motif, l'acheteur est prévenu que c'est pris en charge, et
/// l'exploitation tranche depuis `/api/admin/orders`.
///
/// ON NE TRAITE PAS LES COMMANDES DE REPAS.
///
/// Elles ont leur propre chemin : food-service crée la course quand le repas est
/// PRÊT, pas à la confirmation. Un plat commandé n'existe pas encore.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CreateDeliveryOnOrderConfirmedHandler
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly IDeliveryDispatchApi _dispatch;
    private readonly IOrderingModuleApi _ordering;
    private readonly IInventoryModuleApi _inventory;
    private readonly ISender _sender;
    private readonly ILogger<CreateDeliveryOnOrderConfirmedHandler> _logger;

    public CreateDeliveryOnOrderConfirmedHandler(
        IDeliveryDispatchApi dispatch,
        IOrderingModuleApi ordering,
        IInventoryModuleApi inventory,
        ISender sender,
        ILogger<CreateDeliveryOnOrderConfirmedHandler> logger)
    {
        _dispatch = dispatch;
        _ordering = ordering;
        _inventory = inventory;
        _sender = sender;
        _logger = logger;
    }

    public Task HandleAsync(
        OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
        => string.Equals(e.Kind, "Food", StringComparison.Ordinal)
            ? Task.CompletedTask
            : DemanderCourseAsync(e.OrderId, cancellationToken);

    /// <summary>
    /// Demande la course d'une commande de MARCHANDISE confirmée.
    /// </summary>
    /// <remarks>
    /// PUBLIQUE PARCE QUE L'ARBITRAGE LA REJOUE, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// Quand l'exploitation relance une commande depuis
    /// `POST /api/admin/orders/{id}/review/resume`, il faut refaire exactement
    /// cette étape — devis payé d'abord, repli sans devis, refus argumenté du
    /// multi-lieux compris. La réécrire dans la route en produirait une seconde
    /// version, qui divergerait au premier correctif ; et fabriquer un faux
    /// `OrderConfirmedIntegrationEvent` pour rentrer par `HandleAsync`
    /// donnerait l'illusion qu'une confirmation a eu lieu.
    ///
    /// ELLE NE FILTRE PAS SUR `Kind` : c'est à l'appelant de ne pas
    /// l'appeler pour un repas. La course d'un repas est créée par
    /// food-service quand le sac est PRÊT — vingt à quarante minutes plus tard
    /// — et en poser une ici enverrait un livreur chercher un plat qui n'existe
    /// pas encore.
    /// </remarks>
    public async Task DemanderCourseAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var commande = await _ordering.GetOrderAsync(orderId, cancellationToken);

        if (commande is null)
        {
            _logger.LogError(
                "Commande {OrderId} introuvable à la confirmation. Aucune course demandée : "
                + "elle n'atteindra jamais « livrée » et son vendeur ne sera pas réglé.",
                orderId);

            throw new InvalidOperationException($"Commande {orderId} introuvable.");
        }

        var lieux = commande.Lines
            .Select(l => l.ShipFromLocationId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (lieux.Count == 0)
        {
            _logger.LogError(
                "Commande {OrderId} sans lieu d'expédition sur ses lignes. Aucune course possible.",
                orderId);

            throw new InvalidOperationException($"Commande {orderId} : aucun lieu d'expédition.");
        }

        if (lieux.Count > 1)
        {
            // ═════════════════════════════════════════════════════════════════
            // ON REFUSE DE CRÉER LA COURSE — ET CE REFUS A UNE SORTIE.
            //
            // Le refus lui-même est juste : une course par lieu ferait clore la
            // commande à la première remise, et paierait le vendeur du second
            // colis avant son départ.
            //
            // Ce qui ne l'était pas, c'est ce qui suivait — un `return;` nu. La
            // commande restait `Confirmed` POUR TOUJOURS : ni livraison, ni
            // annulation, ni remboursement, escrow gelé, stock déjà décrémenté,
            // argent encaissé. « Devra être traitée manuellement » supposait
            // qu'un exploitant lise ce journal au bon moment ; rien ne
            // l'attendait nulle part.
            //
            // ON NE REMBOURSE PAS D'OFFICE, ICI NON PLUS.
            //
            // Le multi-colis est une lacune de la plateforme, pas une vente
            // perdue : l'exploitation peut parfaitement faire regrouper les
            // articles sur un seul lieu, puis relancer. Rembourser
            // automatiquement annulerait une commande parfaitement récupérable.
            // ═════════════════════════════════════════════════════════════════
            _logger.LogError(
                "Commande {OrderId} expédiée depuis {Nombre} lieux : le multi-colis n'est pas "
                + "supporté. AUCUNE course créée — la commande passe en ARBITRAGE.",
                orderId, lieux.Count);

            var arbitrage = await _sender.Send(
                new PutOrderUnderReviewCommand(
                    orderId,
                    $"Expédition depuis {lieux.Count} lieux : le multi-colis n'est pas encore "
                    + "supporté. Regrouper les articles sur un seul lieu puis relancer, ou "
                    + "rembourser."),
                cancellationToken);

            SagaOutcome.Exiger(
                arbitrage, _logger,
                "mettre la commande en arbitrage faute de course possible — SANS ELLE, LA "
                + "COMMANDE RESTE CONFIRMÉE POUR TOUJOURS, PAYÉE ET JAMAIS LIVRÉE",
                orderId, lieux.Count);

            return;
        }

        var lieu = await _inventory.GetLocationAsync(lieux[0], cancellationToken);
        var adresse = commande.ShippingAddress;

        var manquants = new List<string>();

        if (lieu is null)
        {
            manquants.Add("lieu d'expédition introuvable");
        }

        if (adresse is null)
        {
            manquants.Add("adresse de livraison du client");
        }

        if (manquants.Count > 0)
        {
            _logger.LogError(
                "Course NON créée pour la commande {OrderId}. Données manquantes : {Manquants}. "
                + "La commande n'atteindra pas « livrée » et son vendeur ne sera pas réglé.",
                orderId, string.Join(", ", manquants));

            throw new InvalidOperationException(
                $"Course impossible pour la commande {orderId} : {string.Join(", ", manquants)}");
        }

        var demande = new CreateDeliveryRequest(
            Reference: OrderDeliveryReference.For(orderId),

            // « HbaExpress » : c'est de la marchandise, pas un repas. Le
            // classement par source porte les statistiques d'exploitation et,
            // demain, des tarifs distincts.
            Source: "HbaExpress",

            // « Standard » ET NON « Express », CONTRAIREMENT AUX REPAS.
            //
            // Un colis attend sans se dégrader ; un plat chaud a une durée de vie
            // de quelques dizaines de minutes. Payer l'express pour un colis
            // serait une dépense sans contrepartie.
            Type: "Standard",
            Pickup: new DeliveryStopRequest(
                ContactName: null,
                Phone: lieu!.ContactPhone,
                Commune: lieu.CommuneName,
                Quartier: lieu.Quartier,
                Landmark: lieu.Landmark,
                Instructions: lieu.Line,
                Latitude: lieu.Latitude,
                Longitude: lieu.Longitude),
            Dropoff: new DeliveryStopRequest(
                ContactName: adresse!.Recipient,
                Phone: adresse.Phone,
                Commune: adresse.CommuneName,
                Quartier: adresse.Quartier,
                Landmark: adresse.Landmark,
                Instructions: adresse.Line1,
                Latitude: adresse.Latitude,
                Longitude: adresse.Longitude),
            Package: new DeliveryPackageRequest(
                Description: $"Commande {commande.Id:N} — {commande.Lines.Count} article(s)",
                WeightKg: null,
                IsFragile: false,
                IsPerishable: false),

            // ═════════════════════════════════════════════════════════════════
            // CE QUE VALENT LES MARCHANDISES — POUR QUE LA COURSE EXIGE UNE
            // PREUVE (ISSUE-057).
            //
            // AVANT, CE HANDLER NE DISAIT RIEN, ET LE CONTRAT VALAIT « None »
            // PAR DÉFAUT : toute course née ici était clôturable sans code, sans
            // photo, sans signature. `RequiredProof` était persisté, projeté vers
            // l'application livreur, et n'avait JAMAIS été renseigné par
            // personne.
            //
            // `Subtotal` ET NON `GrandTotal` : c'est la valeur de ce que le
            // livreur porte. `GrandTotal` inclut les frais de livraison, qui ne
            // sont pas dans le colis — les compter gonflerait la valeur déclarée
            // et ferait basculer en code des courses qui n'en ont pas besoin.
            //
            // C'est delivery-service qui décide de la preuve à partir de là
            // (`ProofPolicy`). Ce service DÉCRIT, il ne conclut pas.
            DeclaredValue: commande.Subtotal,

            // TOUJOURS `false` ICI, ET CE N'EST PAS UN OUBLI.
            //
            // Ce handler ne s'exécute qu'à la CONFIRMATION de la commande,
            // c'est-à-dire après encaissement : rien ne reste à percevoir au
            // seuil de la porte. order-service ne connaît d'ailleurs pas le
            // moyen de paiement — `PaymentMethod`, et sa valeur
            // `CashOnDelivery`, vivent dans payment-service et ne traversent
            // aucun contrat lu ici.
            //
            // CE QUE ÇA NE COUVRE PAS : le jour où la place de marché
            // acceptera le paiement à la livraison, cette ligne deviendra FAUSSE
            // EN SILENCE, et les courses concernées repasseront sous le seuil de
            // la photo alors qu'elles transportent de l'argent. Il faudra alors
            // que le moyen de paiement voyage jusqu'ici.
            IsCashOnDelivery: false,

            // ═════════════════════════════════════════════════════════════════
            // LE DEVIS FIGÉ AU CHECKOUT : C'EST CE QUE LE CLIENT A PAYÉ.
            //
            // CE MONTANT EST DÉSORMAIS CELUI DU DEVIS, PLUS CELUI DE L'ACHETEUR.
            //
            // Ce champ était déjà transmis, mais l'identifiant qu'il portait était
            // DICTÉ par l'acheteur, en même temps qu'un `ShippingFee` qu'il
            // choisissait librement. Il posait zéro, la commande encaissait zéro,
            // et cette ligne achetait quand même la course au prix réel : la
            // plateforme payait la livraison de son client.
            //
            // `PlaceOrderCommandHandler` relit maintenant ce devis auprès de
            // delivery-service et enregistre SON total sur la commande. Le montant
            // facturé à l'acheteur et celui payé ici sont donc le MÊME — c'est le
            // même devis, consommé une seule fois par `CreateDeliveryCommand`, qui
            // rattache son total à la course.
            //
            // EN REDEMANDER UN ICI ACHÈTERAIT LA COURSE À UN PRIX QUE PERSONNE
            // N'A RÉGLÉ. C'est exactement ce que fait le repli ci-dessous, et
            // c'est pour cela qu'il n'est tenté qu'en dernier recours.
            // ═════════════════════════════════════════════════════════════════
            QuoteId: commande.DeliveryQuoteId);

        var course = await _dispatch.CreateAsync(demande, cancellationToken);

        if (course.Succeeded)
        {
            _logger.LogInformation(
                "Course {DeliveryId} créée pour la commande {OrderId}.", course.DeliveryId, orderId);

            return;
        }

        // ═════════════════════════════════════════════════════════════════════
        // C'EST LE SEUL ENDROIT OÙ LE PRIX PAYÉ ET LE PRIX ACHETÉ PEUVENT
        //    ENCORE DIVERGER, ET IL EST DÉLIBÉRÉ.
        //
        // Le devis d'un colis vaut quinze minutes (`DeliveryQuote.Validity`) ;
        // la confirmation suit le checkout du temps d'un paiement mobile. Un
        // devis expiré est donc le refus le plus probable, et il est ordinaire.
        //
        // Deux issues, toutes deux mauvaises : abandonner la course — la commande
        // est payée, le stock décrémenté, et elle n'atteindrait jamais « livrée »,
        // donc le vendeur ne serait jamais réglé — ou acheter la course au prix du
        // moment, en encaissant celui du devis. On choisit la seconde : l'écart
        // est BORNÉ (deux prix réels calculés à quelques minutes d'écart), il est
        // JOURNALISÉ, et il ne se creuse que dans le sens d'une grille qui a
        // changé entre-temps.
        //
        // CE REPLI N'EST PAS LA FAILLE REFERMÉE PLUS HAUT. Celle-là laissait
        // l'ACHETEUR choisir le montant, sans plafond ni trace. Ici, aucun des
        // deux prix ne vient de lui, et les deux sont écrits.
        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        // CE REPLI SE DÉCLENCHAIT SUR N'IMPORTE QUEL MOTIF DE REFUS.
        //
        // La seule condition était « la commande portait un devis ». Un téléphone
        // invalide, une commune inconnue, un quota partenaire atteint — tous
        // relançaient un second appel, journalisé « Devis payé refusé » alors que
        // le devis n'y était pour rien. Sur les motifs qui ne dépendent pas du
        // devis, le second essai échoue à l'identique : on paie un aller-retour
        // pour rien et on écrit une ligne de journal fausse.
        //
        // Ce n'était pas corrigeable avant : le motif arrivait empaqueté dans une
        // chaîne libre (« code — message »), inexploitable. Le code normalisé
        // voyage maintenant dans son propre champ.
        //
        // SEULS LES DEUX MOTIFS QUI DISQUALIFIENT LE DEVIS LUI-MÊME.
        //
        // `pricing.quote_not_usable` (expiré, déjà consommé) et
        // `pricing.quote.malformed` : dans ces cas seuls, redemander la course
        // SANS devis a un sens, et c'est le repli documenté ci-dessus.
        //
        // UNE PANNE DE delivery-pricing N'EN FAIT PLUS PARTIE, ET C'EST UN
        // CHANGEMENT DE COMPORTEMENT ASSUMÉ.
        //
        // `pricing.grpc_*` déclenchait aussi le repli. Or sans devis, la course
        // est créée SANS PRIX — `AttachQuote` n'est pas appelé, `Price` reste nul.
        // On achetait donc, pendant une panne du service tarifaire, une course que
        // personne ne pourra facturer ni sur laquelle calculer un gain livreur.
        // Désormais on lève : l'outbox rejoue, et la course sera créée avec son
        // prix quand le service tarifaire répondra. Le coût est un délai de
        // livraison pendant la panne ; le bénéfice, aucune course impayable.
        // ═════════════════════════════════════════════════════════════════════
        var devisEnCause = course.ReasonCode is "pricing.quote_not_usable" or "pricing.quote.malformed";

        if (devisEnCause && !string.IsNullOrWhiteSpace(commande.DeliveryQuoteId))
        {
            _logger.LogWarning(
                "Devis payé {QuoteId} refusé pour la commande {OrderId} ({Code} — {Motif}). Nouvelle "
                + "tentative sans devis — le prix acheté peut différer de celui payé.",
                commande.DeliveryQuoteId, orderId, course.ReasonCode, course.Reason);

            var secondEssai = await _dispatch.CreateAsync(
                demande with { QuoteId = null }, cancellationToken);

            if (secondEssai.Succeeded)
            {
                _logger.LogInformation(
                    "Course {DeliveryId} créée pour la commande {OrderId}, hors devis payé.",
                    secondEssai.DeliveryId, orderId);

                return;
            }

            course = secondEssai;
        }

        _logger.LogError(
            "Course NON créée pour la commande {OrderId} — {Code} : {Motif}. Le vendeur ne sera pas réglé.",
            orderId, course.ReasonCode, course.Reason);

        // ON LÈVE PLUTÔT QUE DE METTRE EN ARBITRAGE, ET C'EST DÉLIBÉRÉ.
        //
        // Un refus de Delivery est le plus souvent PASSAGER — service en
        // redémarrage, zone momentanément sans grille tarifaire. L'outbox rejoue,
        // et le prochain essai passe. Ouvrir un dossier d'arbitrage au premier
        // refus mettrait une file entière sur le bureau de l'exploitation pour une
        // panne de trente secondes. Le jour où les reprises s'épuisent, la lettre
        // morte porte la trace.
        throw new InvalidOperationException(
            $"Course refusée pour la commande {orderId} : {course.ReasonCode} — {course.Reason}");
    }
}
