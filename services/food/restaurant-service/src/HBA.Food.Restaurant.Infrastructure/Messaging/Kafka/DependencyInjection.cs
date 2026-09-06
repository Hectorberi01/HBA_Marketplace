using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Restaurant.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Food.Restaurant.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Food.Restaurant.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Food.Restaurant.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Food.Restaurant.Infrastructure.Messaging.Kafka.Producers;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Food.Restaurant.Infrastructure.Messaging.Kafka;

/// <summary>
/// LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// Les consommateurs de la plateforme vivaient dans SIX conventions differentes
/// selon le service. Chercher « qui ecoute quoi » supposait de connaître la
/// convention du service qu'on ouvrait.
///
/// LA LIGNE DE PARTAGE : CE DOSSIER PORTE LA POLITIQUE DU SERVICE, LE SOCLE
/// PARTAGE PORTE LE TYPE ET LE PROTOCOLE. `Outbox/` et `Inbox/` contiennent le
/// geste d'enregistrement, PAS une copie de `OutboxMessage` ni de
/// `ConsumerInboxEntry` — ce sont des entites EF dont les tables sont creees par
/// les migrations de ce service.
///
/// CE QUI N'EST PAS ICI :
/// `Serialization/` est absent : ce service n'a pas de convertisseur propre et
/// utilise celui de `HBA.Shared.Infrastructure.Kafka`. Un dossier vide se lirait
/// comme une promesse tenue ailleurs.
/// `Interceptors/` est absent : la correlation et le `traceparent` sont deja
/// portes par l'enveloppe partagee. Un intercepteur local serait une SECONDE
/// implementation du meme contrat.
///
/// L'IDEMPOTENCE reste dans l'installeur quand elle y est : elle sert aussi les
/// routes HTTP annotees `AllowIdempotency()`.
///
/// La justification complete de cette forme est ecrite une seule fois, dans
/// `user-service` — `Messaging/Kafka/DependencyInjection.cs` et
/// `Messaging/Kafka/Outbox/OutboxUsers.cs`. Elle n'est pas recopiee ici.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Branche toute la messagerie du service. Appelee par `Program.cs` ; son
    /// absence est detectee au demarrage par `GardeDeCablage`.
    /// </summary>
    public static IServiceCollection AjouterMessagerieFoodRestaurant(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFoodRestaurant();
        services.AjouterOutboxFoodRestaurant();
        services.AjouterInboxFoodRestaurant();

        // ═════════════════════════════════════════════════════════════════════════
        // DEUX AMONTS POUR UNE SEULE PORTE D'ENTRÉE — ET C'EST DÉLIBÉRÉ.
        //
        // `MealOrderConfirmed` vient de food-order-service, `OrderConfirmed` de la
        // marketplace. Personne ne consommait le premier : une commande passée par le
        // parcours food traversait le paiement sans qu'aucun ticket ne s'ouvre — client
        // débité, aucune cuisine servie, et rien pour le dire puisqu'un événement sans
        // consommateur se consomme en silence.
        //
        // L'ancien reste enregistré LE TEMPS DE LA BASCULE. Tant que le chemin
        // marketplace→food peut porter une commande de repas, le retirer rouvrirait la
        // panne symétrique. Les deux ouvrent le ticket par la MÊME commande applicative,
        // idempotente sur `OrderId` : aucun doublon n'en sort.
        //
        // Il s'enlèvera quand le contrat de confirmation commun sera DÉPLACÉ chez son
        // propriétaire unique — le lot suivant, décrit dans `MealOrderIntegrationEvents`.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<MealOrderConfirmedIntegrationEvent>,
            ReceiveFoodOrderOnMealOrderConfirmedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            ReceiveFoodOrderOnOrderConfirmedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderReadyForPickupIntegrationEvent>,
            CreateDeliveryOnFoodOrderReadyHandler>();

        // CES DEUX-LÀ VONT ENSEMBLE. La remise exige l'état « enlevée » (§20) :
        // n'enregistrer que la seconde produirait un conflit sur chaque commande, et la
        // chaîne resterait rompue au même endroit.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>,
            MarkFoodOrderPickedUpOnDeliveryPickedUpHandler>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
            MarkFoodOrderDeliveredOnDeliveryCompletedHandler>();

        return services;
    }
}
