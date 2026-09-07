using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Payments.Infrastructure.Persistence.Outbox;
using HBA.Financial.Payments.Infrastructure.Persistence.Inbox;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Retry;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Financial.Payments.Infrastructure.Idempotency;

/// <summary>Pose le magasin d'idempotence ET son purgeur, en un seul geste.</summary>
public static class IdempotencyRegistration
{
    public static IServiceCollection AjouterIdempotenceFinancialPayments(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddHostedService<IdempotencyPurger>();

        return services;
    }
}
