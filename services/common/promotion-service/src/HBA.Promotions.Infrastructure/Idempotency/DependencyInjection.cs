using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Promotions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using HBA.Promotions.Infrastructure.Persistence.Outbox;
using HBA.Promotions.Infrastructure.Persistence.Inbox;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Retry;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Promotions.Infrastructure.Idempotency;

/// <summary>Pose le magasin d'idempotence ET son purgeur, en un seul geste.</summary>
public static class IdempotencyRegistration
{
    public static IServiceCollection AjouterIdempotencePromotions(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddHostedService<IdempotencyPurger>();

        return services;
    }
}
