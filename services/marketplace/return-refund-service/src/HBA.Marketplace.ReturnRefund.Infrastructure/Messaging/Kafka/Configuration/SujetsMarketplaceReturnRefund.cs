using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsMarketplaceReturnRefund
{
    private static readonly string[] Sujets =
    [

    ];

    internal static IServiceCollection AjouterSujetsMarketplaceReturnRefund(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
