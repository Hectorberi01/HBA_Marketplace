using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsEngagementRecommendations
{
    private static readonly string[] Sujets =
    [

    ];

    internal static IServiceCollection AjouterSujetsEngagementRecommendations(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
