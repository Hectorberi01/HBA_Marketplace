using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Users.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE user-service ÉCOUTE, NOMMÉS EN ENTIER.</summary>
public static class SujetsUsers
{
    /// <summary>
    /// Le jour où ce service écoutera un second domaine, la ligne à ajouter est
    /// ici, et le gestionnaire qui la justifie dans `Consumers/`.
    /// </summary>
    private static readonly string[] Sujets = ["service.identity.v1"];

    internal static IServiceCollection AjouterSujetsUsers(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
