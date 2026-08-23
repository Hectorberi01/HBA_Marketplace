using HBA.Food.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE HBA DÉCIDE DU DOSSIER D'UN RESTAURATEUR, IL DOIT L'APPRENDRE.
///
/// AUCUNE DE CES TROIS NOTIFICATIONS N'EXISTAIT.
///
/// Le module Food levait ses événements de domaine, correctement, et rien ne les
/// publiait vers l'extérieur : refus de dossier, suspension, levée de suspension
/// n'atteignaient personne. Le restaurateur voyait un statut changer sur son
/// écran, sans motif, et sans savoir quoi corriger.
///
/// C'est le défaut exact relevé côté vendeurs (S5) et corrigé là-bas — reproduit
/// ici en construisant Food sur la forme de ce qui venait d'être réparé, sans en
/// reprendre les corrections.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RestaurantApprovedNotificationHandler
    : IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public RestaurantApprovedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(RestaurantApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.OwnerUserId,
            "Votre établissement est validé",
            $"« {e.Name} » est validé et visible des clients. Vérifiez vos horaires de service et votre carte "
            + "avant votre premier service.",
            "Restaurant",
            e.RestaurantId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le restaurateur que son dossier est REFUSÉ, et lui dit POURQUOI.
///
/// SANS LE MOTIF, LE REFUS EST UNE IMPASSE : il redépose le même dossier, la
/// modération le refuse à nouveau, et les deux s'épuisent.
/// </summary>
public sealed class RestaurantRejectedNotificationHandler
    : IIntegrationEventHandler<RestaurantRejectedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public RestaurantRejectedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(RestaurantRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.OwnerUserId,
            "Dossier refusé",
            string.IsNullOrWhiteSpace(e.Reason)
                ? "Votre dossier d'établissement a été refusé. Corrigez vos informations depuis votre espace, "
                  + "puis soumettez-le de nouveau ; contactez le support si vous ne voyez pas ce qui doit changer."
                : $"Votre dossier d'établissement a été refusé. Motif : {e.Reason}. "
                  + "Corrigez ce point depuis votre espace, puis soumettez-le de nouveau.",
            "Restaurant",
            e.RestaurantId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le restaurateur que son établissement a été SUSPENDU.
///
/// Une suspension le retire de la vitrine : la découvrir par la chute de ses
/// commandes lui ferait perdre des jours à chercher une panne qui n'existe pas.
/// Doublée par e-mail — il n'ouvrira pas forcément l'application ce jour-là, et
/// c'est justement le jour où il doit savoir.
/// </summary>
public sealed class RestaurantSuspendedNotificationHandler
    : IIntegrationEventHandler<RestaurantSuspendedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public RestaurantSuspendedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(RestaurantSuspendedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.OwnerUserId,
            "Établissement suspendu",
            string.IsNullOrWhiteSpace(e.Reason)
                ? "Votre établissement a été suspendu : il n'apparaît plus dans l'application et ne reçoit plus de "
                  + "commandes. Contactez le support pour en connaître le motif."
                : $"Votre établissement a été suspendu : il n'apparaît plus dans l'application et ne reçoit plus de "
                  + $"commandes. Motif : {e.Reason}",
            "Restaurant",
            e.RestaurantId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>Prévient le restaurateur que sa suspension est levée.</summary>
public sealed class RestaurantReopenedNotificationHandler
    : IIntegrationEventHandler<RestaurantReopenedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public RestaurantReopenedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(RestaurantReopenedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.OwnerUserId,
            "Suspension levée",
            "Votre établissement est de nouveau visible des clients. Vérifiez la disponibilité de vos plats "
            + "avant votre prochain service.",
            "Restaurant",
            e.RestaurantId,
            cancellationToken,
            alsoEmail: true);
}
