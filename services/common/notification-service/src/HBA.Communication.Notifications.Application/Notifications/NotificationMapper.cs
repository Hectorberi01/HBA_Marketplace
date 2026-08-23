using HBA.Communication.Notifications.Contracts;
using HBA.Communication.Notifications.Domain.Notifications;

namespace HBA.Communication.Notifications.Application.Notifications;

internal static class NotificationMapper
{
    public static NotificationSummary ToSummary(Notification n) => new(
        n.Id.Value,
        n.RecipientUserId,
        n.Channel.ToString(),
        n.Subject,
        n.Body,
        n.RelatedEntityType,
        n.RelatedEntityId,
        n.Status.ToString(),
        n.CreatedAtUtc,
        n.ReadAtUtc);
}
