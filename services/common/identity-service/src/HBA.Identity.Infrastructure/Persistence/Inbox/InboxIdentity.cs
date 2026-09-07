using HBA.Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Identity.Infrastructure.Persistence.Outbox;
using HBA.Identity.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Identity.Infrastructure.Messaging.Kafka.Retry;
using HBA.Identity.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Identity.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxIdentity
{
    internal static IServiceCollection AjouterInboxIdentity(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
