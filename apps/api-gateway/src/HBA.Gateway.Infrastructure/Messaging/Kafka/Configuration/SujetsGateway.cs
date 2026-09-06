using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// LE SEUL SUJET QUE LA PASSERELLE ECOUTE.
///
/// `TokenRevoked` est publie par identity-service, donc pose sur
/// `service.identity.v1`. Sans cette liste, le consommateur partage se rabat sur
/// les vingt sujets de la plateforme : la passerelle deserialiserait tout le
/// trafic du bus pour en traiter un message sur des milliers.
/// </summary>
public static class SujetsGateway
{
    private static readonly string[] Sujets = ["service.identity.v1"];

    internal static IServiceCollection AjouterSujetsGateway(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
