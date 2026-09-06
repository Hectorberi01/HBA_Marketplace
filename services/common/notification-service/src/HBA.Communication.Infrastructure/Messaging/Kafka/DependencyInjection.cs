using HBA.Communication.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Communication.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Communication.Infrastructure.Messaging.Kafka.Producers;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Infrastructure.Messaging.Kafka;

/// <summary>
/// LE MODULE KAFKA DE LA MESSAGERIE INTERNE — LA DERNIERE EXCEPTION FERMEE.
///
/// Ce module etait le seul des vingt-cinq a ne pas avoir le sien. La raison
/// tenait un temps : il ne consomme rien, et un dossier complet pour une seule
/// ligne utile paraissait cher. Elle coutait pourtant ce que coutent toutes les
/// exceptions — quelqu'un qui cherche le videur d'outbox de ce module ne le
/// trouvait pas la ou les vingt-quatre autres sont.
///
/// `Consumers/` est absent : ce module n'ecoute rien. `Serialization/` et
/// `Interceptors/` aussi, pour les raisons ecrites dans `user-service`.
///
/// LA LIGNE DE PARTAGE : ce dossier porte la POLITIQUE, le socle partage porte
/// le TYPE et le protocole.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterMessagerieCommunication(this IServiceCollection services)
    {
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsCommunication();
        services.AjouterOutboxCommunication();

        return services;
    }
}
