using HBA.Delivery.Pricing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Delivery.Pricing.Infrastructure.Persistence.Outbox;
using HBA.Delivery.Pricing.Infrastructure.Messaging.Kafka.Retry;
using HBA.Delivery.Pricing.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Delivery.Pricing.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxDeliveryPricing
{
    internal static IServiceCollection AjouterOutboxDeliveryPricing(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
