using HBA.Users.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Retry;
using HBA.Users.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Users.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE user-service — LE CÂBLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxUsers
{
    internal static IServiceCollection AjouterOutboxUsers(this IServiceCollection services)
    {
        // `AddOutboxProcessor` enregistre le processeur ET le purgeur, et ne fait
        // rien si `Outbox:Enabled` est à faux — voir `OutboxRegistration`.
        services.AjouterLOutboxLocale();
        return services;
    }
}
