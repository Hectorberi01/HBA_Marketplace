using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Identity.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsIdentity
{
    private static readonly string[] Sujets =
    [
        "service.delivery.v1",

        // DEUX SERVICES PUBLIENT `DriverVerified`, ET IL FAUT LES DEUX.
        "service.driver.v1",
        "service.food.v1",
        "service.merchant.v1"
    ];

    internal static IServiceCollection AjouterSujetsIdentity(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
