using Microsoft.Extensions.Logging;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts;
using HBA.Orders.Contracts;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le client suit son repas, de l'acceptation au départ du livreur.</summary>
/// <summary>À QUI APPARTIENT LA COMMANDE D'UN TICKET DE CUISINE.</summary>
public sealed class AcheteurDuTicket
{
    private readonly IOrderingModuleApi _marketplace;
    private readonly IMealOrderModuleApi _repas;

    public AcheteurDuTicket(IOrderingModuleApi marketplace, IMealOrderModuleApi repas)
    {
        _marketplace = marketplace;
        _repas = repas;
    }

    /// <summary>
    /// L'acheteur, ou <c> null</c> si l'univers indiqué ne connaît pas la commande.
    /// </summary>
    public async Task<Guid?> ResoudreAsync(
        string? origine, Guid orderId, CancellationToken cancellationToken)
    {
        if (string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase))
        {
            return (await _repas.GetOrderAsync(orderId, cancellationToken))?.BuyerId;
        }

        // Un message d'avant le lot 6.4 ne porte pas l'origine et vaut «
        // Marketplace » : exact, puisque aucune commande de repas n'avait pu être
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
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.FoodOrderAcceptedNotificationHandler")]
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
            // Le délai est ce que le client veut savoir en premier.
            $"Le restaurant a accepté votre commande. Préparation estimée : "
            + $"{e.EstimatedPreparationMinutes} minutes.",
            cancellationToken);
}

/// <summary>La préparation a commencé.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.FoodOrderPreparingNotificationHandler")]
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
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.FoodOrderReadyNotificationHandler")]
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
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.FoodOrderPickedUpNotificationHandler")]
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
