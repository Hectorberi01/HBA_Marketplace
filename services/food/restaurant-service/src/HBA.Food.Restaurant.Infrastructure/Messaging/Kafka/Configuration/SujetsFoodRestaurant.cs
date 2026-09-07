using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Food.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsFoodRestaurant
{
    private static readonly string[] Sujets =
    [
        "service.delivery.v1",
        "service.food-order.v1",
        "service.food.v1",
        "service.order.v1"
    ];

    internal static IServiceCollection AjouterSujetsFoodRestaurant(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
