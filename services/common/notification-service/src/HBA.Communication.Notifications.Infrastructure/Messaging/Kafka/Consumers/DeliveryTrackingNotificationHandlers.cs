using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Orders.Contracts;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>L'acheteur suit sa livraison, de l'acceptation à la remise.</summary>
internal static class DeliveryTracking
{
    /// <summary>
    /// Retrouve l'acheteur derrière une référence de course, ou rend `null` si elle
    /// ne nous appartient pas.
    /// </summary>
    public static async Task<Guid?> AcheteurAsync(
        IOrderingModuleApi ordering, string? reference, CancellationToken cancellationToken)
    {
        if (DeliveryReference.ReadOrder(reference) is not { } orderId)
        {
            return null;
        }

        var commande = await ordering.GetOrderAsync(orderId, cancellationToken);
        return commande?.BuyerId;
    }
}

/// <summary>Un livreur a accepté : le client sait que quelqu'un vient.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.DeliveryAcceptedNotificationHandler")]
public sealed class DeliveryAcceptedNotificationHandler
    : IIntegrationEventHandler<DeliveryAcceptedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _ordering;

    public DeliveryAcceptedNotificationHandler(
        NotificationDispatcher dispatcher, IOrderingModuleApi ordering)
    {
        _dispatcher = dispatcher;
        _ordering = ordering;
    }

    public async Task HandleAsync(
        DeliveryAcceptedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await DeliveryTracking.AcheteurAsync(_ordering, e.Reference, cancellationToken)
            is not { } acheteur)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            acheteur, "Livreur trouvé",
            "Un livreur a pris votre course en charge et se rend chez le vendeur.",
            "Delivery", e.DeliveryId, cancellationToken, alsoEmail: false);
    }
}

/// <summary>
/// Le colis est parti : c'est l'étape que le client attend — et c'est le seul
/// moment où le code de remise lui est communiqué.
/// </summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.DeliveryPickedUpNotificationHandler")]
public sealed class DeliveryPickedUpNotificationHandler
    : IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _ordering;
    private readonly ISecretProtector _protecteur;

    public DeliveryPickedUpNotificationHandler(
        NotificationDispatcher dispatcher, IOrderingModuleApi ordering, ISecretProtector protecteur)
    {
        _dispatcher = dispatcher;
        _ordering = ordering;
        _protecteur = protecteur;
    }

    public async Task HandleAsync(
        DeliveryPickedUpIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await DeliveryTracking.AcheteurAsync(_ordering, e.Reference, cancellationToken)
            is not { } acheteur)
        {
            return;
        }

        // ON NE CAPTURE PAS L'ÉCHEC DU DÉCHIFFREMENT.
        var corps = e.ProtectedDeliveryPin is { Length: > 0 } charge
            ? "Votre colis a été récupéré et arrive. Code de remise à donner au livreur : "
              + _protecteur.Unprotect(charge)
            : "Votre colis a été récupéré et arrive.";

        await _dispatcher.NotifyAsync(
            acheteur, "En cours de livraison",
            corps,
            "Delivery", e.DeliveryId, cancellationToken, alsoEmail: false);
    }
}

/// <summary>Aucun livreur après épuisement des tentatives.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.DeliveryNoDriverAlertHandler")]
public sealed class DeliveryNoDriverAlertHandler
    : IIntegrationEventHandler<DeliveryNoDriverAvailableIntegrationEvent>
{
    private readonly ILogger<DeliveryNoDriverAlertHandler> _logger;

    public DeliveryNoDriverAlertHandler(ILogger<DeliveryNoDriverAlertHandler> logger)
        => _logger = logger;

    public Task HandleAsync(
        DeliveryNoDriverAvailableIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "AUCUN LIVREUR pour la course {DeliveryId} après {Tentatives} tentatives "
            + "(référence {Reference}, source {Source}). La marchandise est prête et personne "
            + "ne vient — intervention d'exploitation requise.",
            e.DeliveryId, e.Attempts, e.Reference, e.Source);

        return Task.CompletedTask;
    }
}
