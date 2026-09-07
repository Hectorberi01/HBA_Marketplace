using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Food.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Food.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Food.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Food.Infrastructure.Messaging.Kafka.Producers;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Food.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieFoodRestaurant(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFoodRestaurant();
        services.AjouterOutboxFoodRestaurant();
        services.AjouterInboxFoodRestaurant();

        // DEUX AMONTS POUR UNE SEULE PORTE D'ENTRÉE — ET C'EST DÉLIBÉRÉ.
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
        // n'enregistrer que la seconde produirait un conflit sur chaque commande,
        // et la chaîne resterait rompue au même endroit.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>,
            MarkFoodOrderPickedUpOnDeliveryPickedUpHandler>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
            MarkFoodOrderDeliveredOnDeliveryCompletedHandler>();

        return services;
    }
}
