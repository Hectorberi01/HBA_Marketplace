using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsFinancialPayments
{
    private static readonly string[] Sujets =
    [
        "service.food-order.v1",
        "service.order.v1"
    ];

    internal static IServiceCollection AjouterSujetsFinancialPayments(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
