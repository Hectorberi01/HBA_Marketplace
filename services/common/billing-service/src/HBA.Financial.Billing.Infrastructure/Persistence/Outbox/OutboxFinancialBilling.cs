using HBA.Financial.Billing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Billing.Infrastructure.Persistence.Outbox;
using HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Retry;
using HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxFinancialBilling
{
    internal static IServiceCollection AjouterOutboxFinancialBilling(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
