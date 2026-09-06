using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Inventory.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// LES SUJETS QUE CE SERVICE ECOUTE.
///
/// Ils sont deduits des 0 evenement(s) declares dans `Consumers/` et du service
/// qui les publie — le sujet porte le domaine du PRODUCTEUR, jamais celui du
/// consommateur (voir `HbaTopics`).
///
/// SANS CETTE LISTE, `SubscribeTopics` reste vide et le consommateur partage se
/// rabat sur les vingt sujets de la plateforme : le service desserialise tout et
/// jette presque tout.
///
/// UN GESTIONNAIRE DANS `Consumers/` DONT LE SUJET MANQUE ICI NE SERA JAMAIS
/// APPELE, en silence. Aucun compilateur ne relie les deux.
///
/// CE SERVICE NE CONSOMME AUCUN EVENEMENT : la liste est vide, et c'est exact.
///
/// CE QUE LA LISTE VIDE NE FAIT PAS ENCORE. Le consommateur partage traite
/// « liste vide » comme « pas de liste » et se rabat sur les vingt sujets de la
/// plateforme. Ce service continue donc a tout recevoir et a tout jeter — sans
/// gestionnaire, l'effet est nul, mais le trafic reste. Fermer cela demande de
/// distinguer les deux cas dans `KafkaIntegrationEventConsumer`, ce qui touche
/// les vingt-trois services a la fois : ce n'est pas fait ici.
/// </summary>
public static class SujetsInventory
{
    private static readonly string[] Sujets =
    [

    ];

    internal static IServiceCollection AjouterSujetsInventory(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
