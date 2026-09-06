using HBA.FoodCarts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodCarts.Infrastructure.Persistence.Outbox;
using HBA.FoodCarts.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Inbox.Persistence.Outbox;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Inbox.Persistence.Inbox;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Inbox.Messaging.Kafka.Retry;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Inbox.Messaging.Kafka.Processors;
namespace HBA.FoodCarts.Infrastructure.Messaging.Kafka.Inbox;

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
public static class InboxFoodCart
{
    internal static IServiceCollection AjouterInboxFoodCart(this IServiceCollection services)
    {
        services.AddScoped<IConsumerInbox, EfConsumerInbox>();
        return services;
    }
}
