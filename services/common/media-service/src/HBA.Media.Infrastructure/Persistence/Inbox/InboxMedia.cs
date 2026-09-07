using HBA.Media.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Media.Infrastructure.Persistence.Outbox;
using HBA.Media.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Media.Infrastructure.Messaging.Kafka.Retry;
using HBA.Media.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Media.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxMedia
{
    internal static IServiceCollection AjouterInboxMedia(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
