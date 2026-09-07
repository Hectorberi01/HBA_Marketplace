using HBA.Food.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Producers;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Promotions.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessageriePromotions(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsPromotions();
        services.AjouterOutboxPromotions();
        services.AjouterInboxPromotions();

        // LES DEUX COMPENSATIONS DU §10.16.
        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            ReleaseCouponsOnOrderCancelledHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>,
            ReleaseCouponsOnFoodOrderCancelledHandler>();

        return services;
    }
}
