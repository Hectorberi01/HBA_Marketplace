using HBA.Food.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Food.Infrastructure.Persistence.Outbox;
using HBA.Food.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Food.Infrastructure.Messaging.Kafka.Retry;
using HBA.Food.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Food.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxFoodRestaurant
{
    internal static IServiceCollection AjouterInboxFoodRestaurant(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
