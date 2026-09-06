using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// LA MESSAGERIE INTERNE N'ECOUTE RIEN, ET C'EST DECLARE PLUTOT QUE SOUS-ENTENDU.
///
/// Ce module publie `MessageSent` et ne consomme aucun evenement. La liste est
/// donc vide, et elle DIT « rien », la ou son absence disait « je n'ai pas encore
/// ete migre » — deux choses que le consommateur partage ne distinguait pas.
///
/// CE QUE ÇA NE COUPE PAS ICI. `HBA.Communication.Api` compose ce module AVEC
/// celui des notifications, qui declare dix sujets. L'union est faite par la
/// fabrique d'options : le consommateur de l'hote demarre bien, et cette liste
/// vide n'y retranche rien.
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
