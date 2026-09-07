using HBA.Drivers.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Drivers.Infrastructure.Persistence.Outbox;
using HBA.Drivers.Infrastructure.Messaging.Kafka.Retry;
using HBA.Drivers.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Drivers.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxDeliveryDriver
{
    internal static IServiceCollection AjouterOutboxDeliveryDriver(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
