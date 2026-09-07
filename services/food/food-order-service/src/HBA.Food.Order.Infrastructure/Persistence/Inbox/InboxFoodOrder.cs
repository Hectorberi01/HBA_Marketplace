using HBA.FoodOrders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodOrders.Infrastructure.Persistence.Outbox;
using HBA.FoodOrders.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Retry;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxFoodOrder
{
    internal static IServiceCollection AjouterInboxFoodOrder(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
