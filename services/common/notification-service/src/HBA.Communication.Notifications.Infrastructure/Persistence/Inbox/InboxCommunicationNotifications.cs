using HBA.Communication.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Communication.Notifications.Infrastructure.Persistence.Outbox;
using HBA.Communication.Notifications.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxCommunicationNotifications
{
    internal static IServiceCollection AjouterInboxCommunicationNotifications(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
