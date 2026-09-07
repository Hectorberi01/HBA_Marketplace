using HBA.FoodOrders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodOrders.Infrastructure.Persistence.Outbox;
using HBA.FoodOrders.Infrastructure.Persistence.Inbox;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Retry;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxFoodOrder
{
    internal static IServiceCollection AjouterOutboxFoodOrder(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
