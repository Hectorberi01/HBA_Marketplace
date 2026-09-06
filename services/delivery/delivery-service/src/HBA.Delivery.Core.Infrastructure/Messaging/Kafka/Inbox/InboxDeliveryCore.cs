using HBA.Shared.Infrastructure.Inbox;
using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Deliveries.Infrastructure.Messaging.Kafka.Inbox;

/// <summary>
/// L'INBOX DE CE SERVICE — LA GARDE CONTRE LE DOUBLE TRAITEMENT.
///
/// Kafka livre AU MOINS UNE FOIS. Sans cet enregistrement, un rebalancement de
/// partition ou une remise a zero d'offsets rejoue les evenements deja traites :
/// la table `consumer_inbox` existe dans le schema du service, et personne ne la
/// lit.
///
/// CE QU'ELLE NE COUVRE PAS. Elle dedoublonne la CONSOMMATION, pas les effets
/// deja partis : un evenement traite a moitie laisse un etat que rien ne
/// rattrape ici.
///
/// La justification complete de cette forme est ecrite une seule fois, dans
/// `user-service` — `Messaging/Kafka/DependencyInjection.cs` et
/// `Messaging/Kafka/Outbox/OutboxUsers.cs`. Elle n'est pas recopiee ici.
/// </summary>
public static class InboxDeliveryCore
{
    internal static IServiceCollection AjouterInboxDeliveryCore(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox<DeliveriesDbContext>>();
        return services;
    }
}
