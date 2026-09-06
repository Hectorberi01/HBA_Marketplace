using HBA.Shared.Infrastructure.Outbox;
using HBA.FoodOrders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>
/// L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.
///
/// Sans le processeur enregistre ci-dessous, les evenements listes dans
/// `Producers/EvenementsPublies` s'ecrivent en base et n'en sortent jamais. La
/// panne est SILENCIEUSE : la transaction metier reussit, l'appelant recoit son
/// 200, et le consommateur d'en face attend un message qui ne viendra pas.
///
/// CE FICHIER NE CONTIENT NI `OutboxMessage`, NI `OutboxProcessor`, NI
/// `OutboxRetryPolicy`. Ce sont des types du socle partage : `OutboxMessage` est
/// une entite EF dont la table est creee par les migrations de ce service, et
/// `ModuleDbContext.SaveChangesAsync` y ecrit dans la transaction metier. Une
/// copie locale divergerait de la colonne reelle en silence.
///
/// La justification complete de cette forme est ecrite une seule fois, dans
/// `user-service` — `Messaging/Kafka/DependencyInjection.cs` et
/// `Messaging/Kafka/Outbox/OutboxUsers.cs`. Elle n'est pas recopiee ici.
/// </summary>
public static class OutboxFoodOrder
{
    internal static IServiceCollection AjouterOutboxFoodOrder(this IServiceCollection services)
    {
        services.AddOutboxProcessor<MealOrderingDbContext>();
        return services;
    }
}
