using HBA.Financial.Wallet.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Wallet.Infrastructure.Persistence.Outbox;
using HBA.Financial.Wallet.Infrastructure.Persistence.Inbox;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Retry;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxFinancialWallet
{
    internal static IServiceCollection AjouterOutboxFinancialWallet(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
