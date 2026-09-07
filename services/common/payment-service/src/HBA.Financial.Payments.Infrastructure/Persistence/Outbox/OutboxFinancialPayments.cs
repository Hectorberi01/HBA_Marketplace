using HBA.Financial.Payments.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Payments.Infrastructure.Persistence.Outbox;
using HBA.Financial.Payments.Infrastructure.Persistence.Inbox;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Retry;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxFinancialPayments
{
    internal static IServiceCollection AjouterOutboxFinancialPayments(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
