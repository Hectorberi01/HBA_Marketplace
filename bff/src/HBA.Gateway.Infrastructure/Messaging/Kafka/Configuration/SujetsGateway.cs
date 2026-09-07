using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LE SEUL SUJET QUE LA PASSERELLE ECOUTE.</summary>
public static class SujetsGateway
{
    private static readonly string[] Sujets = ["service.identity.v1"];

    internal static IServiceCollection AjouterSujetsGateway(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
