using HBA.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Inventory.Infrastructure.Persistence.Outbox;
using HBA.Inventory.Infrastructure.Messaging.Kafka.Retry;
using HBA.Inventory.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Inventory.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxInventory
{
    internal static IServiceCollection AjouterOutboxInventory(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
