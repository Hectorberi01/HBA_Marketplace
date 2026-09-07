using HBA.Commerce.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Commerce.Infrastructure.Persistence.Outbox;
using HBA.Commerce.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Retry;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Commerce.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxCommerce
{
    internal static IServiceCollection AjouterInboxCommerce(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
