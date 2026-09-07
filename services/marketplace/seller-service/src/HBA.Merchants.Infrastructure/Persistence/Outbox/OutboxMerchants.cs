using HBA.Merchants.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Merchants.Infrastructure.Persistence.Outbox;
using HBA.Merchants.Infrastructure.Persistence.Inbox;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Retry;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Merchants.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxMerchants
{
    internal static IServiceCollection AjouterOutboxMerchants(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
