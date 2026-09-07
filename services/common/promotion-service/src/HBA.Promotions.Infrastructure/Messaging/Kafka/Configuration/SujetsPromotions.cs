using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Promotions.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsPromotions
{
    private static readonly string[] Sujets =
    [
        "service.food.v1",
        "service.order.v1"
    ];

    internal static IServiceCollection AjouterSujetsPromotions(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
