using HBA.Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Identity.Infrastructure.Persistence.Outbox;
using HBA.Identity.Infrastructure.Persistence.Inbox;
using HBA.Identity.Infrastructure.Messaging.Kafka.Retry;
using HBA.Identity.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Identity.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxIdentity
{
    internal static IServiceCollection AjouterOutboxIdentity(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
