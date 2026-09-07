using HBA.Communication.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Communication.Notifications.Infrastructure.Persistence.Outbox;
using HBA.Communication.Notifications.Infrastructure.Persistence.Inbox;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxCommunicationNotifications
{
    internal static IServiceCollection AjouterOutboxCommunicationNotifications(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
