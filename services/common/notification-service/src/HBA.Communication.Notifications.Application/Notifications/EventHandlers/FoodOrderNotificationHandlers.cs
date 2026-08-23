using Microsoft.Extensions.Logging;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts;
using HBA.Orders.Contracts;
using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Le client suit son repas, de l'acceptation au départ du livreur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ENTRE LE PAIEMENT ET LA LIVRAISON, LE CLIENT NE VOYAIT RIEN.
///
/// Les sept événements du cycle de vie d'un repas n'avaient aucun consommateur.
/// Un acheteur payait, recevait « Commande confirmée », puis plus rien pendant
/// quarante minutes — pas même « le restaurant a accepté ». C'est le moment où
/// l'on appelle le support.
///
/// QUATRE ÉTAPES NOTIFIÉES SUR SEPT, ET LES TROIS AUTRES SONT DÉLIBÉRÉES.
///
///   • `FoodOrderReceived` — le client vient de recevoir « commande confirmée ».
///     Un second message dans la même seconde n'apprend rien et use l'attention
///     qu'on aura besoin de capter plus tard.
///
///   • `FoodOrderRejected` et `FoodOrderCancelled` — ils annulent la commande,
///     et l'annulation publie `OrderCancelled`, déjà notifié. Notifier ici
///     enverrait DEUX messages pour un seul fait, avec le risque qu'ils se
///     contredisent le jour où l'un des deux textes changera.
///
/// La règle : un fait, une notification, chez celui qui possède le fait.
///
/// LE `BuyerId` N'EST PAS DANS L'ÉVÉNEMENT — ON RELIT LA COMMANDE.
///
/// Les événements de food-service portent `OrderId`, pas l'acheteur : Food ne
/// connaît pas les comptes. Élargir l'événement pour le confort du service de
/// notification en ferait un engagement envers tous les consommateurs, présents
/// et futurs. Une lecture gRPC de plus sur une transition d'état coûte moins.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// À QUI APPARTIENT LA COMMANDE D'UN TICKET DE CUISINE.
///
/// ÉCRIT PARCE QUE LE CLIENT DU NOUVEAU PARCOURS NE RECEVAIT AUCUN SUIVI.
///
/// Les quatre gestionnaires de ce fichier lisaient la commande chez
/// order-service — seul univers qu'ils connaissaient. Or le ticket de cuisine
/// naît de deux ponts, et son `OrderId` peut être celui d'une `MealOrder`.
/// Pour toutes ces commandes-là, la lecture rendait `null` : « commande
/// introuvable », un Warning, et rien n'était envoyé. Acceptation, mise en
/// préparation, repas prêt, repas récupéré — quatre notifications, aucune
/// délivrée, sans qu'aucune alerte ne se déclenche.
///
/// C'est le même défaut que la création de course, en plus discret : là-bas le
/// gestionnaire levait, ici il se contentait d'un Warning parce qu'une
/// notification manquée ne bloque ni argent ni repas. Le silence était donc
/// complet.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class AcheteurDuTicket
{
    private readonly IOrderingModuleApi _marketplace;
    private readonly IMealOrderModuleApi _repas;

    public AcheteurDuTicket(IOrderingModuleApi marketplace, IMealOrderModuleApi repas)
    {
        _marketplace = marketplace;
        _repas = repas;
    }

    /// <summary>L'acheteur, ou <c>null</c> si l'univers indiqué ne connaît pas la commande.</summary>
    public async Task<Guid?> ResoudreAsync(
        string? origine, Guid orderId, CancellationToken cancellationToken)
    {
        if (string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase))
        {
            return (await _repas.GetOrderAsync(orderId, cancellationToken))?.BuyerId;
        }

        // Un message d'avant le lot 6.4 ne porte pas l'origine et vaut
        // « Marketplace » : exact, puisque aucune commande de repas n'avait pu être
        // confirmée avant que le lot 6.1 n'ouvre son chemin de paiement.
        return (await _marketplace.GetOrderAsync(orderId, cancellationToken))?.BuyerId;
    }
}

internal static class FoodOrderNotification
{
    public static async Task NotifierAcheteurAsync(
        NotificationDispatcher dispatcher,
        AcheteurDuTicket acheteurs,
        ILogger logger,
        string? origine,
        Guid orderId,
        string titre,
        string message,
        CancellationToken cancellationToken)
    {
        var acheteur = await acheteurs.ResoudreAsync(origine, orderId, cancellationToken);

        if (acheteur is null)
        {
            // ON NE LÈVE PAS POUR UNE NOTIFICATION.
            //
            // Contrairement à un remboursement ou à une création de course, un
            // message manqué ne laisse ni argent ni repas en suspens. Faire
            // rejouer le message trois fois puis alerter en Critical pour une
            // commande introuvable serait disproportionné.
            logger.LogWarning(
                "Notification « {Titre} » non envoyée : commande {OrderId} introuvable dans "
                + "l'univers « {Origine} ».",
                titre, orderId, origine);

            return;
        }

        await dispatcher.NotifyAsync(
            acheteur.Value, titre, message, "Order", orderId, cancellationToken, alsoEmail: false);
    }
}

/// <summary>Le restaurant a accepté : le client sait que son repas se fera.</summary>
public sealed class FoodOrderAcceptedNotificationHandler
    : IIntegrationEventHandler<FoodOrderAcceptedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AcheteurDuTicket _acheteurs;
    private readonly ILogger<FoodOrderAcceptedNotificationHandler> _logger;

    public FoodOrderAcceptedNotificationHandler(
        NotificationDispatcher dispatcher,
        AcheteurDuTicket acheteurs,
        ILogger<FoodOrderAcceptedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _acheteurs = acheteurs;
        _logger = logger;
    }

    public Task HandleAsync(
        FoodOrderAcceptedIntegrationEvent e, CancellationToken cancellationToken = default)
        => FoodOrderNotification.NotifierAcheteurAsync(
            _dispatcher, _acheteurs, _logger, e.OrderOrigin, e.OrderId,
            "Commande acceptée",
            // Le délai est ce que le client veut savoir en premier. L'annoncer
            // ici évite l'appel au support dix minutes plus tard.
            $"Le restaurant a accepté votre commande. Préparation estimée : "
            + $"{e.EstimatedPreparationMinutes} minutes.",
            cancellationToken);
}

/// <summary>La préparation a commencé.</summary>
public sealed class FoodOrderPreparingNotificationHandler
    : IIntegrationEventHandler<FoodOrderPreparingIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AcheteurDuTicket _acheteurs;
    private readonly ILogger<FoodOrderPreparingNotificationHandler> _logger;

    public FoodOrderPreparingNotificationHandler(
        NotificationDispatcher dispatcher,
        AcheteurDuTicket acheteurs,
        ILogger<FoodOrderPreparingNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _acheteurs = acheteurs;
        _logger = logger;
    }

    public Task HandleAsync(
        FoodOrderPreparingIntegrationEvent e, CancellationToken cancellationToken = default)
        => FoodOrderNotification.NotifierAcheteurAsync(
            _dispatcher, _acheteurs, _logger, e.OrderOrigin, e.OrderId,
            "En préparation",
            "Votre repas est en cours de préparation.",
            cancellationToken);
}

/// <summary>Le repas est prêt : un livreur est cherché.</summary>
public sealed class FoodOrderReadyNotificationHandler
    : IIntegrationEventHandler<FoodOrderReadyForPickupIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AcheteurDuTicket _acheteurs;
    private readonly ILogger<FoodOrderReadyNotificationHandler> _logger;

    public FoodOrderReadyNotificationHandler(
        NotificationDispatcher dispatcher,
        AcheteurDuTicket acheteurs,
        ILogger<FoodOrderReadyNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _acheteurs = acheteurs;
        _logger = logger;
    }

    public Task HandleAsync(
        FoodOrderReadyForPickupIntegrationEvent e, CancellationToken cancellationToken = default)
        => FoodOrderNotification.NotifierAcheteurAsync(
            _dispatcher, _acheteurs, _logger, e.OrderOrigin, e.OrderId,
            "Repas prêt",
            "Votre repas est prêt. Un livreur est en route pour le récupérer.",
            cancellationToken);
}

/// <summary>Le livreur a le repas : il arrive.</summary>
public sealed class FoodOrderPickedUpNotificationHandler
    : IIntegrationEventHandler<FoodOrderPickedUpIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AcheteurDuTicket _acheteurs;
    private readonly ILogger<FoodOrderPickedUpNotificationHandler> _logger;

    public FoodOrderPickedUpNotificationHandler(
        NotificationDispatcher dispatcher,
        AcheteurDuTicket acheteurs,
        ILogger<FoodOrderPickedUpNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _acheteurs = acheteurs;
        _logger = logger;
    }

    public Task HandleAsync(
        FoodOrderPickedUpIntegrationEvent e, CancellationToken cancellationToken = default)
        => FoodOrderNotification.NotifierAcheteurAsync(
            _dispatcher, _acheteurs, _logger, e.OrderOrigin, e.OrderId,
            "En route",
            "Votre repas a été récupéré par le livreur et arrive.",
            cancellationToken);
}
