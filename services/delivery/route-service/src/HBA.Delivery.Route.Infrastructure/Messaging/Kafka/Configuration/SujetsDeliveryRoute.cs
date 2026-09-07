using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Routes.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsDeliveryRoute
{
    private static readonly string[] Sujets =
    [

    ];

    internal static IServiceCollection AjouterSujetsDeliveryRoute(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
