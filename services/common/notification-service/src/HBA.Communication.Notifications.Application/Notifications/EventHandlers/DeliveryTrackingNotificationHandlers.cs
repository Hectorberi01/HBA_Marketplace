using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Orders.Contracts;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// L'acheteur suit sa livraison, de l'acceptation à la remise.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// communication-service NE CONSOMMAIT AUCUN ÉVÉNEMENT DE LIVRAISON.
///
/// Ni l'acceptation, ni la collecte, ni la remise. L'acheteur payait, puis
/// n'entendait plus parler de sa commande jusqu'à ce qu'un livreur sonne — alors
/// que le suivi de livraison est précisément l'information qu'il regarde le
/// plus, et la première raison d'appeler le support.
///
/// ON NE NOTIFIE QUE CE QU'ON SAIT RATTACHER.
///
/// Delivery rend une RÉFÉRENCE opaque : « ORDER-… », « FOOD-… », « SHIP-… », ou
/// la chaîne d'un partenaire externe dont nous ne savons rien. Seules les deux
/// premières désignent une commande HBA dont on connaît l'acheteur.
///
/// Une course de partenaire passe donc sans notification, et c'est le
/// comportement correct : notifier un acheteur qui n'existe pas dans notre base
/// n'a pas de sens.
///
/// LA CONVENTION DE RÉFÉRENCE VIENT DU SOCLE, PAS D'UNE TROISIÈME COPIE.
///
/// order-service et food-service la portaient chacun. En ajouter une ici aurait
/// fait trois façons de découper la même chaîne — et le monolithe avait déjà
/// écrit ce qui arrive alors : « des expéditions qui n'avancent plus, sans
/// erreur ».
///
/// PAS DE NOTIFICATION SUR `DeliveryCompleted`.
///
/// La remise fait passer la commande à « livrée », qui publie `OrderDelivered`.
/// Notifier ici enverrait deux messages pour un même fait. Un fait, une
/// notification.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal static class DeliveryTracking
{
    /// <summary>
    /// Retrouve l'acheteur derrière une référence de course, ou rend `null` si
    /// elle ne nous appartient pas.
    /// </summary>
    /// <remarks>
    /// POUR UNE COURSE DE REPAS, LA RÉFÉRENCE EST CELLE DU TICKET.
    ///
    /// Elle ne permet donc pas de retrouver la commande directement. Ce cas est
    /// traité par les notifications du parcours Food, qui partent de
    /// food-service et disposent de l'`OrderId`. Ici on ne traite que les
    /// courses marketplace — les seules dont la référence porte la commande.
    /// </remarks>
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
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ÉCRIT PARCE QUE LE CODE DE REMISE N'ATTEIGNAIT JAMAIS SON DESTINATAIRE.
///
/// delivery-service tirait un PIN, et `ProofOfDelivery.Capture` le vérifiait à
/// l'arrivée. Entre les deux, personne ne le portait au client. Le livreur
/// sonnait, réclamait un code que le destinataire n'avait jamais vu, et la
/// course restait ouverte : un contrôle correct que rien ne rendait applicable.
/// Ce handler est le bout manquant de la chaîne.
///
/// LE CODE EST DANS LA NOTIFICATION, PAS DANS UN E-MAIL.
///
/// `alsoEmail: false` est conservé : le client est en train de suivre sa course
/// dans l'application, c'est là qu'il regarde. Un doublon par e-mail multiplierait
/// les copies en clair d'un justificatif de remise sans rien apporter.
///
/// CE QUE CE HANDLER NE COUVRE PAS.
///
/// Il n'y a AUCUNE seconde chance automatique. Si la notification n'arrive pas —
/// jeton de push périmé, appareil éteint et notification expirée — le
/// destinataire doit passer par le support, qui relit le code en base. Un renvoi
/// à la demande reste à écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
        //
        // Une charge illisible signifie que delivery-service et ce service n'ont
        // pas la même `Security:SecretProtection:Key`. Envoyer alors le message
        // sans le code — ou avec une valeur de remplacement — donnerait au client
        // une notification qui a l'air normale et un livreur qu'il ne peut pas
        // recevoir. L'exception laisse le message rejouable et se voit.
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

/// <summary>
/// Aucun livreur après épuisement des tentatives.
/// </summary>
/// <remarks>
/// ON NE PRÉVIENT PAS L'ACHETEUR, ET C'EST DÉLIBÉRÉ.
///
/// Le contrat de l'événement le dit : la course reste vivante et reprenable par
/// un opérateur. Annoncer un échec à l'acheteur alors qu'un humain peut encore
/// la pourvoir ferait perdre une vente pour rien.
///
/// Ce gestionnaire existe pour rendre le fait VISIBLE côté exploitation — ce qui
/// est déjà plus que le silence d'avant.
/// </remarks>
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
