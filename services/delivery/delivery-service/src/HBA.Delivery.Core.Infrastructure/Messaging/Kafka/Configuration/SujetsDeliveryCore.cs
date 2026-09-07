using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Deliveries.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsDeliveryCore
{
    private static readonly string[] Sujets =
    [
        "service.delivery.v1",
        "service.driver.v1"
    ];

    internal static IServiceCollection AjouterSujetsDeliveryCore(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
