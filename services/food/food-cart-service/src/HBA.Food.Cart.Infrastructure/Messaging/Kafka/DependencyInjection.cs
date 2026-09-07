using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Configuration;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Consumers;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Inbox;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Outbox;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Producers;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.FoodCarts.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieFoodCart(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFoodCart();
        services.AjouterOutboxFoodCart();
        services.AjouterInboxFoodCart();

        // Chorégraphie : le panier se clôt quand la commande de repas est partie.
        services.AddScoped<
            IIntegrationEventHandler<MealOrderPlacedIntegrationEvent>,
            CloseFoodCartOnMealOrderPlacedHandler>();

        return services;
    }
}
