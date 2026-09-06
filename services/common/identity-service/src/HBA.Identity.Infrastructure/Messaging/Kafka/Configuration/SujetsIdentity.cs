using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Identity.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// LES SUJETS QUE CE SERVICE ECOUTE.
///
/// Ils sont deduits des 5 evenement(s) declares dans `Consumers/` et du service
/// qui les publie — le sujet porte le domaine du PRODUCTEUR, jamais celui du
/// consommateur (voir `HbaTopics`).
///
/// SANS CETTE LISTE, `SubscribeTopics` reste vide et le consommateur partage se
/// rabat sur les vingt sujets de la plateforme : le service desserialise tout et
/// jette presque tout.
///
/// UN GESTIONNAIRE DANS `Consumers/` DONT LE SUJET MANQUE ICI NE SERA JAMAIS
/// APPELE, en silence. Aucun compilateur ne relie les deux.
/// </summary>
public static class SujetsIdentity
{
    private static readonly string[] Sujets =
    [
        "service.delivery.v1",

        // DEUX SERVICES PUBLIENT `DriverVerified`, ET IL FAUT LES DEUX.
        //
        // driver-service le publie depuis `DriverAccountDomainEventHandlers`,
        // delivery-service depuis `DeliveryDomainEventHandlers` : le meme
        // evenement part sur DEUX sujets, celui de chaque producteur. N'en
        // declarer qu'un — ce que la migration avait fait — laisse
        // `GrantDriverRoleHandler` muet une fois sur deux, sans erreur.
        //
        // CE QUE CA NE REGLE PAS. Un fait metier publie par deux services reste
        // une anomalie de contrat : le role serait accorde DEUX FOIS si les deux
        // producteurs emettent pour le meme livreur. L'inbox l'absorbe (les deux
        // messages ont des identifiants differents, donc non), il faut trancher
        // qui est proprietaire de ce fait.
        "service.driver.v1",
        "service.food.v1",
        "service.merchant.v1"
    ];

    internal static IServiceCollection AjouterSujetsIdentity(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
