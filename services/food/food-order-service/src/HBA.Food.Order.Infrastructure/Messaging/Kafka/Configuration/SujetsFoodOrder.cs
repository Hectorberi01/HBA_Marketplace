using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsFoodOrder
{
    private static readonly string[] Sujets =
    [
        "service.delivery.v1",
        "service.financial.v1",
        "service.food.v1"
    ];

    internal static IServiceCollection AjouterSujetsFoodOrder(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
