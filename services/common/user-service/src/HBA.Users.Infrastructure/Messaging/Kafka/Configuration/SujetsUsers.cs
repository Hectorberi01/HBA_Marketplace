using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Users.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES SUJETS QUE user-service ÉCOUTE, NOMMÉS EN ENTIER.
///
/// user-service consomme TROIS types d'événement, tous produits par
/// identity-service. Il s'abonnait pourtant aux VINGT sujets de la plateforme,
/// faute d'un code qui remplisse `SubscribeTopics` : le consommateur partagé se
/// rabat sur `HbaTopics.Tous` quand la liste est vide.
///
/// CE FICHIER EST LA MOITIÉ D'UN COUPLE, ET C'EST SA FAIBLESSE.
///
/// Un gestionnaire déclaré dans `Consumers/` dont le sujet MANQUE ici ne sera
/// JAMAIS appelé — pas d'erreur, pas d'avertissement : l'événement n'arrive
/// simplement pas. Aucun compilateur ne relie les deux. `DependencyInjection`
/// les appelle l'un après l'autre pour qu'on ne puisse pas toucher à l'un sans
/// voir l'autre ; c'est faible, et c'est tout ce qu'on a.
///
/// CE QUE CE FICHIER NE CONTIENT PAS. Ni serveurs, ni groupe de consommation, ni
/// préfixe de sujet : tout cela vient de `KafkaEventBusOptions`, liée à la
/// section « Kafka » de la configuration, donc des variables d'environnement du
/// déploiement. Redéclarer ces valeurs ici en ferait une SECONDE source de
/// vérité, et c'est précisément la classe de panne qu'on passe la semaine à
/// réparer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
