using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using HBA.Communication.Notifications.Infrastructure.Persistence.Outbox;
using HBA.Communication.Notifications.Infrastructure.Persistence.Inbox;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Communication.Notifications.Infrastructure.Idempotency;

/// <summary>Pose le magasin d'idempotence ET son purgeur, en un seul geste.</summary>
public static class IdempotencyRegistration
{
    public static IServiceCollection AjouterIdempotenceCommunicationNotifications(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddHostedService<IdempotencyPurger>();

        return services;
    }
}
