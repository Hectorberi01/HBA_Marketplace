using HBA.Food.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>CE QUE HBA DÉCIDE DU DOSSIER D'UN RESTAURATEUR, IL DOIT L'APPRENDRE.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.RestaurantApprovedNotificationHandler")]
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

/// <summary>Prévient le restaurateur que son dossier est REFUSÉ, et lui dit POURQUOI.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.RestaurantRejectedNotificationHandler")]
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

/// <summary>Prévient le restaurateur que son établissement a été SUSPENDU.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.RestaurantSuspendedNotificationHandler")]
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
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.RestaurantReopenedNotificationHandler")]
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
