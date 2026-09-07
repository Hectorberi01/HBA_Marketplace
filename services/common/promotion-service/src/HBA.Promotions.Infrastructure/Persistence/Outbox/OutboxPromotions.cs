using HBA.Promotions.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Promotions.Infrastructure.Persistence.Outbox;
using HBA.Promotions.Infrastructure.Persistence.Inbox;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Retry;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Promotions.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxPromotions
{
    internal static IServiceCollection AjouterOutboxPromotions(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
