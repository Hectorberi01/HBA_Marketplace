using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Deliveries.Infrastructure.Persistence.Outbox;
using HBA.Deliveries.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Retry;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Deliveries.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxDeliveryCore
{
    internal static IServiceCollection AjouterInboxDeliveryCore(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
