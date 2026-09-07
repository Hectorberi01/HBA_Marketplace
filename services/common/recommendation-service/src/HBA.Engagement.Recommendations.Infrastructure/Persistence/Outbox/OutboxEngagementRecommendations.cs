using HBA.Engagement.Recommendations.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Engagement.Recommendations.Infrastructure.Persistence.Outbox;
using HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Retry;
using HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxEngagementRecommendations
{
    internal static IServiceCollection AjouterOutboxEngagementRecommendations(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
