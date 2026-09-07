using HBA.Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Retry;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Catalog.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxCatalog
{
    internal static IServiceCollection AjouterOutboxCatalog(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
