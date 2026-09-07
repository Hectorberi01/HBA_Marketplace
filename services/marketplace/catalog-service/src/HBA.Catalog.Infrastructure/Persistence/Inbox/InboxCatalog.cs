using HBA.Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Retry;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Catalog.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxCatalog
{
    internal static IServiceCollection AjouterInboxCatalog(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
