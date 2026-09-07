using HBA.Commerce.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Commerce.Infrastructure.Persistence.Outbox;
using HBA.Commerce.Infrastructure.Persistence.Inbox;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Retry;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Commerce.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxCommerce
{
    internal static IServiceCollection AjouterOutboxCommerce(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
