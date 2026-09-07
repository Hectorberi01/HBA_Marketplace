using HBA.FoodCarts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodCarts.Infrastructure.Persistence.Outbox;
using HBA.FoodCarts.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Retry;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.FoodCarts.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxFoodCart
{
    internal static IServiceCollection AjouterInboxFoodCart(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
