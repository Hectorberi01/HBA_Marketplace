using HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieEngagementRecommendations(this IServiceCollection services)
    {
        services.AjouterSujetsEngagementRecommendations();
        services.AjouterOutboxEngagementRecommendations();

        return services;
    }
}
