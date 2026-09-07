using HBA.FoodCarts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodCarts.Infrastructure.Persistence.Outbox;
using HBA.FoodCarts.Infrastructure.Persistence.Inbox;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Retry;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.FoodCarts.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxFoodCart
{
    internal static IServiceCollection AjouterOutboxFoodCart(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
