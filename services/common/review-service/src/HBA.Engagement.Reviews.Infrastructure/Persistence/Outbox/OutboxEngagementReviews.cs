using HBA.Engagement.Reviews.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Engagement.Reviews.Infrastructure.Persistence.Outbox;
using HBA.Engagement.Reviews.Infrastructure.Messaging.Kafka.Retry;
using HBA.Engagement.Reviews.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Engagement.Reviews.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxEngagementReviews
{
    internal static IServiceCollection AjouterOutboxEngagementReviews(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
