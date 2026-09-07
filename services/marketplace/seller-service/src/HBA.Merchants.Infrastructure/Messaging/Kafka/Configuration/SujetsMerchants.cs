using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Merchants.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsMerchants
{
    private static readonly string[] Sujets =
    [
        "service.engagement.v1",
        "service.identity.v1",
        "service.order.v1"
    ];

    internal static IServiceCollection AjouterSujetsMerchants(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
