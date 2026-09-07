using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Users.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Retry;
using HBA.Users.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Users.Infrastructure.Idempotency;

/// <summary>Pose le magasin d'idempotence ET son purgeur, en un seul geste.</summary>
public static class IdempotencyRegistration
{
    public static IServiceCollection AjouterIdempotenceUsers(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddHostedService<IdempotencyPurger>();

        return services;
    }
}
