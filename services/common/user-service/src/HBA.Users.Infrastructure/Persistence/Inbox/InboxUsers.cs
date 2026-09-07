using HBA.Users.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Users.Infrastructure.Messaging.Kafka.Retry;
using HBA.Users.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Users.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE user-service — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxUsers
{
    internal static IServiceCollection AjouterInboxUsers(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
