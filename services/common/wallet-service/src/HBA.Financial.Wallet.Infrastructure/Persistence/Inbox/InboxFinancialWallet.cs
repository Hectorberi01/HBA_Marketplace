using HBA.Financial.Wallet.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Wallet.Infrastructure.Persistence.Outbox;
using HBA.Financial.Wallet.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Retry;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxFinancialWallet
{
    internal static IServiceCollection AjouterInboxFinancialWallet(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
