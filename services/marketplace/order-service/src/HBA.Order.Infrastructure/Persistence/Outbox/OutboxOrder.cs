using HBA.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Orders.Infrastructure.Persistence.Outbox;
using HBA.Orders.Infrastructure.Persistence.Inbox;
using HBA.Orders.Infrastructure.Messaging.Kafka.Retry;
using HBA.Orders.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Orders.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxOrder
{
    internal static IServiceCollection AjouterOutboxOrder(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
