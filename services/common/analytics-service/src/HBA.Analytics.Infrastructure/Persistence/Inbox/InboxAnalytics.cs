using HBA.Analytics.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.</summary>
public static class InboxAnalytics
{
    internal static IServiceCollection AjouterInboxAnalytics(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        services.AddHostedService<InboxCleanupService>();
        return services;
    }
}
