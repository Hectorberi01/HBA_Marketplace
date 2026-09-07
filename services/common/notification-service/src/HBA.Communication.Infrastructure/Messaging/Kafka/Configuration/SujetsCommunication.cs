using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// LA MESSAGERIE INTERNE N'ECOUTE RIEN, ET C'EST DECLARE PLUTOT QUE SOUS-ENTENDU.
/// </summary>
public static class SujetsCommunication
{
    private static readonly string[] Sujets = [];

    internal static IServiceCollection AjouterSujetsCommunication(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
