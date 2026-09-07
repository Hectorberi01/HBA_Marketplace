using HBA.Promotions.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Promotions.Infrastructure.Persistence.Outbox;
using HBA.Promotions.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Retry;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Promotions.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxPromotions
{
    internal static IServiceCollection AjouterInboxPromotions(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
