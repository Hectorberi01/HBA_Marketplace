using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Deliveries.Infrastructure.Persistence.Outbox;
using HBA.Deliveries.Infrastructure.Persistence.Inbox;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Retry;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Deliveries.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxDeliveryCore
{
    internal static IServiceCollection AjouterOutboxDeliveryCore(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
