using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// LES SUJETS QUE CE SERVICE ECOUTE.
///
/// Ils sont deduits des 47 evenement(s) declares dans `Consumers/` et du service
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
/// AUCUN PRODUCTEUR CONNU POUR : ShipmentDeliveredIntegrationEvent, ShipmentShippedIntegrationEvent.
/// Ces evenements sont consommes par ce service et publies par PERSONNE dans
/// le depot. Le gestionnaire correspondant ne sera donc jamais appele. Aucun
/// sujet n'a ete ajoute pour eux : il n'y a rien a ecouter.
/// </summary>
public static class SujetsCommunicationNotifications
{
    private static readonly string[] Sujets =
    [
        // AJOUTE AVEC LES NOTIFICATIONS D'APPROBATION ET DE REFUS DE FICHE.
        // catalog-service publiait `ProductApproved` et `ProductRejected` sans que
        // personne les ecoute ; le vendeur ne savait pas que sa fiche etait passee.
        "service.catalog.v1",
        "service.communication.v1",
        "service.delivery.v1",
        "service.engagement.v1",
        "service.financial.v1",
        "service.food.v1",
        "service.identity.v1",
        "service.inventory.v1",
        "service.merchant.v1",
        "service.order.v1",
        "service.return-refund.v1"
    ];

    internal static IServiceCollection AjouterSujetsCommunicationNotifications(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
