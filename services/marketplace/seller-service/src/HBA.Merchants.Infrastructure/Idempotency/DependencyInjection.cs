using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Merchants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using HBA.Merchants.Infrastructure.Persistence.Outbox;
using HBA.Merchants.Infrastructure.Persistence.Inbox;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Retry;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Merchants.Infrastructure.Idempotency;

/// <summary>Pose le magasin d'idempotence ET son purgeur, en un seul geste.</summary>
public static class IdempotencyRegistration
{
    public static IServiceCollection AjouterIdempotenceMerchants(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddHostedService<IdempotencyPurger>();

        return services;
    }
}
