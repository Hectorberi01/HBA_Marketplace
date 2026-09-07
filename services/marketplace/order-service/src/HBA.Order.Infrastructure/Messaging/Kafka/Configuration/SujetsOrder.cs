using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsOrder
{
    private static readonly string[] Sujets =
    [
        "service.delivery.v1",
        "service.financial.v1",
        "service.food.v1",
        "service.order.v1",
        "service.return-refund.v1"
    ];

    internal static IServiceCollection AjouterSujetsOrder(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
