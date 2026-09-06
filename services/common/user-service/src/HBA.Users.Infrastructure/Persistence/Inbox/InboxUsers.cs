using HBA.Users.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.Users.Infrastructure.Messaging.Kafka.Inbox.Persistence.Outbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Inbox.Persistence.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Inbox.Messaging.Kafka.Retry;
using HBA.Users.Infrastructure.Messaging.Kafka.Inbox.Messaging.Kafka.Processors;
namespace HBA.Users.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'INBOX DE user-service — LA GARDE CONTRE LE DOUBLE TRAITEMENT.
///
/// Kafka livre AU MOINS UNE FOIS. Sans cet enregistrement, rejouer une partition
/// recrée les profils, réapplique les renommages et rejoue les purges : la table
/// `consumer_inbox` existe dans le schéma du service, et personne ne la lit.
///
/// C'EST EXACTEMENT LA MANIPULATION QUI ARRIVE. La remise à zéro des offsets du
/// groupe `hba-user-service` sur `service.identity.v1`, prévue pour rattraper les
/// `user.registered` perdus, REJOUE tout ce que le sujet contient encore. C'est
/// cette ligne qui fait la différence entre un rattrapage et un doublon.
///
/// CE QUE CE FICHIER NE CONTIENT PAS. Ni `ConsumerInboxEntry`, ni sa
/// configuration EF, ni un `InboxProcessor` local : l'entrée d'inbox est une
/// entité mappée par `UsersDbContext` et créée par ses migrations. La raison
/// complète est écrite dans `Outbox/OutboxUsers` et vaut à l'identique ici.
///
/// CE QU'IL NE COUVRE PAS. L'inbox déduplique la CONSOMMATION, pas les effets
/// déjà partis : un événement traité puis rejoué ne repassera pas, mais un
/// événement traité à moitié — profil créé, appel gRPC échoué — laisse un état
/// que rien ne rattrape ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class InboxUsers
{
    internal static IServiceCollection AjouterInboxUsers(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
