using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Configuration;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Inbox;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Outbox;
using HBA.FoodOrders.Infrastructure.Messaging.Kafka.Producers;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieFoodOrder(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFoodOrder();
        services.AjouterOutboxFoodOrder();
        services.AjouterInboxFoodOrder();

        // ── Ce que la commande écoute : le paiement ─────────────────────────
        services.AddScoped<
            IIntegrationEventHandler<PaymentCapturedIntegrationEvent>,
            ConfirmMealOrderOnPaymentCapturedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            CancelMealOrderOnPaymentFailedHandler>();

        // ── Ce que la commande écoute : la cuisine ──────────────────────────
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>,
            CancelMealOrderOnKitchenRejectionHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>,
            CancelMealOrderOnKitchenCancellationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>,
            MarkMealOrderDeliveredOnKitchenDeliveryHandler>();

        // LA PORTE D'ENTRÉE DE L'ARBITRAGE, QUI N'EXISTAIT PAS (ISSUE-061).
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>,
            HoldMealOrderOnDeliveryCancelledHandler>();

        return services;
    }
}
