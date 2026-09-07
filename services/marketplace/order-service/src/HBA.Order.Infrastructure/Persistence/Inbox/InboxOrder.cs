using HBA.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Orders.Infrastructure.Persistence.Outbox;
using HBA.Orders.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Orders.Infrastructure.Messaging.Kafka.Retry;
using HBA.Orders.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Orders.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxOrder
{
    internal static IServiceCollection AjouterInboxOrder(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
