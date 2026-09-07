using HBA.Food.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Food.Infrastructure.Persistence.Outbox;
using HBA.Food.Infrastructure.Persistence.Inbox;
using HBA.Food.Infrastructure.Messaging.Kafka.Retry;
using HBA.Food.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Food.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxFoodRestaurant
{
    internal static IServiceCollection AjouterOutboxFoodRestaurant(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
