using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsFinancialWallet
{
    private static readonly string[] Sujets =
    [
        "service.delivery.v1",
        "service.order.v1",
        "service.return-refund.v1"
    ];

    internal static IServiceCollection AjouterSujetsFinancialWallet(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
