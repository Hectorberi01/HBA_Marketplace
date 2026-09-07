using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Notifications.Contracts.IntegrationEvents;

/// <summary>
/// Une notification est partie chez le fournisseur (§10.15, `notification.sent`).
/// </summary>
[HbaEvent("notification.sent", Version = 1, AggregateType = "Notification")]
public sealed record NotificationSentIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationId { get; init; }

    public required Guid RecipientUserId { get; init; }

    /// <summary>`IN_APP`, `EMAIL`, `SMS` ou `PUSH`.</summary>
    public required string Channel { get; init; }

    /// <summary>Code du gabarit utilisé, ex.</summary>
    public string? TemplateCode { get; init; }

    /// <summary>Identifiant rendu par le fournisseur.</summary>
    public string? ProviderMessageId { get; init; }
}

/// <summary>L'envoi a échoué (§10.15, `notification.failed`).</summary>
[HbaEvent("notification.failed", Version = 1, AggregateType = "Notification")]
public sealed record NotificationFailedIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationId { get; init; }

    public required Guid RecipientUserId { get; init; }

    public required string Channel { get; init; }

    /// <summary>
    /// `PROVIDER_REJECTED`, `TEMPLATE_MISSING`, `PLACEHOLDER_MISSING`,
    /// `RECIPIENT_UNREACHABLE`, `OPTED_OUT`.
    /// </summary>
    public required string Reason { get; init; }

    public string? TemplateCode { get; init; }
}
