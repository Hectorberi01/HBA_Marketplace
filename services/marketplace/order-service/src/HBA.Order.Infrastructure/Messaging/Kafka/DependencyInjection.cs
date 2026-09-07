using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Orders.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Orders.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Orders.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Orders.Infrastructure.Messaging.Kafka.Producers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Orders.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieOrder(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsOrder();
        services.AjouterOutboxOrder();
        services.AjouterInboxOrder();

        // LES DEUX SENS DE LA COURSE, ENFIN BRANCHÉS.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>,
            HoldOrderOnDeliveryCancelledHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            CancelDeliveryOnOrderCancelledHandler>();

        // Suite du Saga : réactions aux résultats de paiement.
        services.AddScoped<
            IIntegrationEventHandler<PaymentCapturedIntegrationEvent>,
            ConfirmOrderOnPaymentCapturedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            CancelOrderOnPaymentFailedHandler>();

        // Étape finale du Saga : la course terminée clôt la commande (déclenche
        // escrow + payout vendeur).
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
            MarkOrderDeliveredOnDeliveryCompletedHandler>();

        // LE RESTAURANT REFUSE → LA COMMANDE EST ANNULÉE.
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>,
            CancelOrderOnFoodOrderRejectedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>,
            CancelOrderOnFoodOrderCancelledHandler>();

        // LE REPAS EST REMIS AU CLIENT → LA COMMANDE EST LIVRÉE.
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>,
            MarkOrderDeliveredOnFoodOrderDeliveredHandler>();

        // SANS CETTE LIGNE, LA COMMANDE N'APPREND JAMAIS QU'UN ARTICLE EST REVENU.
        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundedIntegrationEvent>,
            RecordReturnSettlementOnRefundHandler>();

        // ENREGISTREMENT INTROUVABLE DANS LE COMPOSITION ROOT. Ce gestionnaire
        // existe et n'etait enregistre nulle part : il n'a jamais ete appele.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            CreateDeliveryOnOrderConfirmedHandler>();

        return services;
    }
}
