using HBA.Financial.Payments.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Payments.Infrastructure.Persistence.Outbox;
using HBA.Financial.Payments.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Retry;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxFinancialPayments
{
    internal static IServiceCollection AjouterInboxFinancialPayments(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
