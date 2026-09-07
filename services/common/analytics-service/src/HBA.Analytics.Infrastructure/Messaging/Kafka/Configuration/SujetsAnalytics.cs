using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsAnalytics
{
    private static readonly string[] Sujets =
    [
        "service.order.v1",
        "service.merchant.v1",
        "service.identity.v1",
        "service.financial.v1",
        "service.driver.v1"
    ];

    internal static IServiceCollection AjouterSujetsAnalytics(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
