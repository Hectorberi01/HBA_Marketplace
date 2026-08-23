using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Notifications.Contracts.IntegrationEvents;

/// <summary>
/// Une notification est partie chez le fournisseur (§10.15, `notification.sent`).
///
/// « PARTIE » N'EST PAS « REÇUE ».
///
/// L'événement dit que le fournisseur a accepté le message, pas que le
/// destinataire l'a vu. La confirmation de livraison arrive plus tard, par webhook,
/// et n'est pas ce fait-ci. Confondre les deux ferait conclure « le client a été
/// prévenu » alors que son téléphone est éteint depuis trois jours.
/// </summary>
[HbaEvent("notification.sent", Version = 1, AggregateType = "Notification")]
public sealed record NotificationSentIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationId { get; init; }

    public required Guid RecipientUserId { get; init; }

    /// <summary>`IN_APP`, `EMAIL`, `SMS` ou `PUSH`.</summary>
    public required string Channel { get; init; }

    /// <summary>Code du gabarit utilisé, ex. `food.order.accepted`.</summary>
    public string? TemplateCode { get; init; }

    /// <summary>
    /// Identifiant rendu par le fournisseur. Il sert au rapprochement quand un
    /// webhook de livraison arrive — c'est la seule clé commune entre les deux.
    /// </summary>
    public string? ProviderMessageId { get; init; }
}

/// <summary>
/// L'envoi a échoué (§10.15, `notification.failed`).
///
/// IL PORTE LA RAISON, PAS LE CONTENU.
///
/// Rejouer un message échoué demande de le reconstruire à partir du gabarit et de
/// ses valeurs, pas de le relire dans un événement. Y placer le corps rendu ferait
/// transiter par Kafka des textes qui nomment des personnes, des montants et des
/// adresses — exactement ce que le §19.7 interdit.
/// </summary>
[HbaEvent("notification.failed", Version = 1, AggregateType = "Notification")]
public sealed record NotificationFailedIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationId { get; init; }

    public required Guid RecipientUserId { get; init; }

    public required string Channel { get; init; }

    /// <summary>
    /// `PROVIDER_REJECTED`, `TEMPLATE_MISSING`, `PLACEHOLDER_MISSING`,
    /// `RECIPIENT_UNREACHABLE`, `OPTED_OUT`. Le code permet de distinguer ce qui se
    /// rejoue de ce qui ne se rejouera jamais — un opt-out rejoué indéfiniment est
    /// du harcèlement, pas de la résilience.
    /// </summary>
    public required string Reason { get; init; }

    public string? TemplateCode { get; init; }
}
