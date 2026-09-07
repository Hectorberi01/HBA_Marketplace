using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Contracts.IntegrationEvents;

/// <summary>
/// Un message a été envoyé. Consommé par Notifications (alerte temps réel au
/// destinataire).
/// </summary>
[HbaEvent("communication.message.sent")]
public sealed record MessageSentIntegrationEvent : IntegrationEvent
{
    public required Guid ConversationId { get; init; }
    public required Guid MessageId { get; init; }
    public required Guid SenderId { get; init; }
}
