using HBA.Deliveries.Contracts;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Application.Orders.EventHandlers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;

namespace HBA.Orders.Api.Integration;

/// <summary>
/// La convention qui empêche la boucle entre « commande annulée » et « course
/// annulée ».
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX SENS SONT DÉSORMAIS BRANCHÉS, ET ILS SE MORDENT LA QUEUE.
///
///   • une course annulée met la commande en ARBITRAGE
///     (<see cref="HoldOrderOnDeliveryCancelledHandler"/>) ;
///   • une commande annulée annule sa course
///     (<see cref="CancelDeliveryOnOrderCancelledHandler"/>).
///
/// Sans marqueur, le second déclencherait le premier : la commande annulée fait
/// annuler la course, `DeliveryCancelled` revient, et l'on tenterait de remettre
/// en arbitrage une commande déjà close.
///
/// CE MARQUEUR N'EST PAS LA SEULE PROTECTION, ET C'EST VOULU.
///
/// `Order.MarkUnderReview` refuse déjà les états terminaux — la boucle serait
/// donc cassée par le domaine, avec un code `ordering.already_terminal` que
/// l'appelant absorbe. Mais elle serait cassée BRUYAMMENT, à chaque annulation :
/// un aller-retour réseau et une ligne de journal pour un non-événement. Le
/// marqueur la coupe avant, et le domaine reste le garde-fou de dernier recours.
///
/// Le motif voyage par ailleurs jusqu'au webhook partenaire : il doit rester
/// lisible par un humain, d'où un préfixe et non un code opaque.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class OrderDeliveryCancellation
{
    /// <summary>Préfixe posé quand c'est NOUS qui annulons la course.</summary>
    public const string ReasonPrefix = "Commande annulée";

    public static string Motif(string? raison)
        => string.IsNullOrWhiteSpace(raison) ? ReasonPrefix : $"{ReasonPrefix} — {raison}";

    public static bool EstNotreFait(string? raison)
        => raison is not null && raison.StartsWith(ReasonPrefix, StringComparison.Ordinal);
}

/// <summary>
/// La course a été annulée → la commande passe en ARBITRAGE.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `DeliveryCancelled` N'AVAIT QU'UN SEUL CONSOMMATEUR, ET IL ÉTAIT INTERNE.
///
/// `WebhookOnDeliveryCancelled`, dans delivery-service, prévenait les partenaires
/// EXTERNES. Rien ne remontait à order-service ni à food-service : une course
/// annulée disparaissait, et la commande qui en dépendait restait `Confirmed`
/// POUR TOUJOURS — ni livraison, ni annulation, ni remboursement, escrow gelé,
/// stock déjà décrémenté, argent encaissé, et un acheteur qui attend un colis que
/// personne n'apportera.
///
/// ON N'ANNULE PAS ET ON NE REMBOURSE PAS.
///
/// Une course annulée est le plus souvent RÉATTRIBUABLE : livreur en panne, refus
/// après acceptation, erreur de dispatch, colis non prêt à l'heure. Rembourser
/// d'office détruirait des ventes parfaitement récupérables — et l'argent rendu ne
/// se reprend pas. La commande entre donc dans une file d'arbitrage, où un humain
/// choisit entre relancer une course et retourner la vente.
///
/// CE GESTIONNAIRE LIT LES DEUX PRÉFIXES, ET C'EST UNE EXCEPTION ASSUMÉE.
///
/// Ailleurs, order-service ne relit que `ORDER-` et laisse food-service traduire
/// `FOOD-` — voir `MarkOrderDeliveredOnFoodOrderDeliveredHandler`. Cette règle
/// tient parce que le retour de course de repas doit d'abord faire AVANCER le
/// ticket de cuisine, ce que seul food-service sait faire.
///
/// Ici, rien ne doit avancer côté cuisine, et surtout PAS le ticket : le sac est
/// prêt, il reste sur le passe, et une nouvelle course peut venir le chercher. Le
/// faire passer par food-service supposerait un geste sur le ticket — or le seul
/// disponible, `CancelFoodOrderCommand`, publie `FoodOrderCancelled`, que
/// order-service consomme en ANNULANT la commande. Autrement dit : le chemin
/// « propre » aurait produit exactement le remboursement automatique qu'on
/// refuse.
///
/// La traduction `FOOD-` → commande se fait donc ici, par
/// <c>IFoodModuleApi.GetOrderAsync</c> — un contrat qui existe précisément « pour
/// le retour de course », et qui ne rend que des rattachements.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class HoldOrderOnDeliveryCancelledHandler
    : IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IFoodModuleApi _food;
    private readonly ILogger<HoldOrderOnDeliveryCancelledHandler> _logger;

    public HoldOrderOnDeliveryCancelledHandler(
        ISender sender,
        IFoodModuleApi food,
        ILogger<HoldOrderOnDeliveryCancelledHandler> logger)
    {
        _sender = sender;
        _food = food;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // C'EST NOUS QUI VENONS D'ANNULER CETTE COURSE. Remettre la commande
        // en arbitrage serait rouvrir un dossier sur une commande close. Voir
        // `OrderDeliveryCancellation`.
        if (OrderDeliveryCancellation.EstNotreFait(e.Reason))
        {
            return;
        }

        var orderId = await ResoudreCommandeAsync(e, cancellationToken);

        if (orderId is not { } commande)
        {
            // Expédition du monolithe (`SHIP-`) ou course d'un partenaire
            // externe. Rien à faire, et surtout rien à journaliser : ce chemin
            // est emprunté par la majorité des événements de course.
            return;
        }

        var motif = string.IsNullOrWhiteSpace(e.Reason)
            ? "La course a été annulée."
            : $"La course a été annulée : {e.Reason}";

        var resultat = await _sender.Send(
            new PutOrderUnderReviewCommand(commande, motif), cancellationToken);

        // « DÉJÀ EN ARBITRAGE » N'EST PAS UN ÉCHEC.
        //
        // L'outbox livre au moins une fois, et une commande peut voir deux
        // courses annulées coup sur coup. Traiter le second passage en erreur
        // ferait sortir une alerte pour un dossier correctement ouvert.
        if (resultat.IsFailure && resultat.Error.Code == "ordering.already_under_review")
        {
            _logger.LogDebug(
                "Commande {OrderId} déjà en arbitrage — course {DeliveryId} annulée, rien à faire.",
                commande, e.DeliveryId);

            return;
        }

        // Même raisonnement pour une commande déjà close : une course annulée
        // après la remise, ou après un remboursement décidé, ne rouvre rien.
        if (resultat.IsFailure
            && resultat.Error.Code is "ordering.already_terminal" or "ordering.already_delivered")
        {
            _logger.LogInformation(
                "Course {DeliveryId} annulée sur une commande {OrderId} déjà close ({Code}). "
                + "Aucun arbitrage ouvert.",
                e.DeliveryId, commande, resultat.Error.Code);

            return;
        }

        SagaOutcome.Exiger(
            resultat, _logger,
            "mettre la commande en arbitrage après l'annulation de sa course — SANS ELLE, LA "
            + "COMMANDE RESTE CONFIRMÉE POUR TOUJOURS, PAYÉE ET JAMAIS LIVRÉE",
            commande, e.DeliveryId, e.Reference);
    }

    /// <summary>
    /// De quelle commande commerciale s'agit-il ? <c>null</c> pour tout ce qui
    /// n'est pas à nous — le cas le plus fréquent.
    /// </summary>
    private async Task<Guid?> ResoudreCommandeAsync(
        DeliveryCancelledIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (OrderDeliveryReference.Read(e.Reference) is { } direct)
        {
            return direct;
        }

        if (DeliveryReference.ReadFoodOrder(e.Reference) is not { } ticket)
        {
            return null;
        }

        var repas = await _food.GetOrderAsync(ticket, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // CE TICKET N'EST PEUT-ÊTRE PAS LE NÔTRE, ET ON NE POUVAIT PAS LE
        // SAVOIR.
        //
        // Le préfixe `FOOD-` encode l'identifiant du TICKET DE CUISINE, pas celui
        // d'une commande. Or le ticket naît de deux ponts : une commande de ce
        // service, ou une `MealOrder` de food-order-service. Son `OrderId`
        // venait donc de deux univers sans discriminant, et cette méthode rendait
        // parfois un identifiant que ce service ne connaît pas — envoyé tel quel
        // à `PutOrderUnderReviewCommand`.
        //
        // L'arbitrage d'une commande de repas est désormais tenu par
        // `HoldMealOrderOnDeliveryCancelledHandler`, chez son propriétaire. Ici on
        // sort en silence : ce n'est pas une anomalie, c'est la moitié du trafic.
        // ═════════════════════════════════════════════════════════════════════
        if (repas is not null
            && string.Equals(repas.Origin, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (repas is null)
        {
            // ON LÈVE : UNE RÉFÉRENCE `FOOD-` DÉSIGNE FORCÉMENT UN TICKET DE
            // CETTE PLATEFORME.
            //
            // L'introuvable ne peut venir que d'une indisponibilité de
            // food-service — cause passagère, qui aboutira au prochain essai.
            // Sortir en silence laisserait une commande de repas payée bloquée
            // pour toujours, très exactement le défaut qu'on referme.
            _logger.LogError(
                "Ticket de cuisine {FoodOrderId} introuvable après l'annulation de la course "
                + "{DeliveryId}. La commande de repas ne peut pas être mise en arbitrage.",
                ticket, e.DeliveryId);

            throw new InvalidOperationException(
                $"Ticket de cuisine {ticket} introuvable : arbitrage impossible.");
        }

        return repas.OrderId;
    }
}

/// <summary>
/// La commande a été annulée → sa course l'est aussi.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉCIPROQUE MANQUAIT, ET ELLE ENVOYAIT DES LIVREURS DANS LE VIDE.
///
/// `IDeliveryDispatchApi` n'apparaissait dans TOUT order-service qu'à la création
/// de la course. Annuler une commande ne touchait donc jamais la course : un
/// livreur partait chercher un colis que le vendeur ne remettrait pas. Trajet à
/// vide, livreur immobilisé sur une mission morte, et personne sur place pour le
/// lui expliquer — sur une plateforme où la disponibilité des livreurs est la
/// ressource rare.
///
/// « AUCUNE COURSE » EST LE CAS LE PLUS FRÉQUENT, ET IL EST NORMAL.
///
/// La course n'est créée qu'à la CONFIRMATION. Or `Order.Cancel` refuse une
/// commande confirmée : les annulations ordinaires arrivent donc toutes avant
/// qu'aucune course n'existe. Les courses réellement concernées sont celles des
/// commandes closes par une autre voie — refus du restaurant
/// (`RejectByProvider`), ou arbitrage tranché vers le remboursement
/// (`CancelAfterReview`). Journaliser ce cas en erreur ferait sonner une alerte à
/// chaque annulation de panier.
///
/// ON NE TRAITE QUE LES COURSES `ORDER-`.
///
/// Une commande de repas annulée passe par food-service, qui possède le ticket et
/// la course `FOOD-` qui va avec. La traduction inverse est possible ici, mais
/// l'annulation d'une course de repas doit s'accompagner de l'arrêt de la
/// cuisine — un geste qui appartient à son propriétaire, pas à nous.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CancelDeliveryOnOrderCancelledHandler
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly IDeliveryDispatchApi _dispatch;
    private readonly ILogger<CancelDeliveryOnOrderCancelledHandler> _logger;

    public CancelDeliveryOnOrderCancelledHandler(
        IDeliveryDispatchApi dispatch, ILogger<CancelDeliveryOnOrderCancelledHandler> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var resultat = await _dispatch.CancelByReferenceAsync(
            OrderDeliveryReference.For(e.OrderId),

            // La même source qu'à la création — voir
            // `CreateDeliveryOnOrderConfirmedHandler`. Delivery indexe la
            // référence PAR source ; se tromper ici rendrait « introuvable » une
            // course qui existe.
            source: "HbaExpress",
            OrderDeliveryCancellation.Motif(e.Reason),
            cancellationToken);

        if (!resultat.Found)
        {
            // Aucune course : le cas normal. Voir l'en-tête.
            return;
        }

        if (!resultat.Cancelled)
        {
            // COLIS DÉJÀ COLLECTÉ, OU COURSE DÉJÀ TERMINÉE.
            //
            // Le domaine de Delivery refuse d'annuler à ce stade, et il a raison :
            // le colis est physiquement en circulation. Mais il roule pour une
            // commande annulée, et personne ne s'en apercevrait sans cette ligne.
            // C'est un retour à organiser, donc une alerte d'exploitation — pas un
            // rejeu, que l'issue soit la même à chaque essai.
            _logger.LogError(
                "Commande {OrderId} annulée mais sa course {Reference} n'a PAS pu l'être "
                + "({Motif}). Un colis circule pour une commande annulée : retour à organiser.",
                e.OrderId, OrderDeliveryReference.For(e.OrderId), resultat.Reason);

            return;
        }

        _logger.LogInformation(
            "Course de la commande {OrderId} annulée avec elle : aucun livreur n'ira chercher "
            + "un colis qui ne partira pas.",
            e.OrderId);
    }
}
