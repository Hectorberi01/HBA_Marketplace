using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Outbox;
using HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Retry;
using HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxMarketplaceReturnRefund
{
    internal static IServiceCollection AjouterOutboxMarketplaceReturnRefund(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
