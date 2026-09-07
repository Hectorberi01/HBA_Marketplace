using HBA.Communication.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Communication.Infrastructure.Persistence.Outbox;
using HBA.Communication.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Communication.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>
/// L'OUTBOX DE LA MESSAGERIE INTERNE — LE CABLAGE ICI, LE TYPE ET LA TABLE
/// AILLEURS.
/// </summary>
public static class OutboxCommunication
{
    internal static IServiceCollection AjouterOutboxCommunication(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
