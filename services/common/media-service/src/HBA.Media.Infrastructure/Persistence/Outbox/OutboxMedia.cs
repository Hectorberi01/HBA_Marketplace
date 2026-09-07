using HBA.Media.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Media.Infrastructure.Persistence.Outbox;
using HBA.Media.Infrastructure.Persistence.Inbox;
using HBA.Media.Infrastructure.Messaging.Kafka.Retry;
using HBA.Media.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Media.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxMedia
{
    internal static IServiceCollection AjouterOutboxMedia(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
