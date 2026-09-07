using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Catalog.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsCatalog
{
    private static readonly string[] Sujets =
    [
        "service.inventory.v1",
        "service.merchant.v1"
    ];

    internal static IServiceCollection AjouterSujetsCatalog(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
