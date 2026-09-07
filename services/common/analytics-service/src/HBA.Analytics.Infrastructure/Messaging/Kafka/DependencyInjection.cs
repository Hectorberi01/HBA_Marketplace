using HBA.Analytics.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Analytics.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieAnalytics(this IServiceCollection services)
    {
        services.AjouterSujetsAnalytics();
        services.AjouterInboxAnalytics();

        // CHAQUE GESTIONNAIRE, ET SON SUJET EN REGARD.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            OrderConfirmedRollUpHandler>();                        // service.order.v1

        services.AddScoped<
            IIntegrationEventHandler<SellerRegisteredIntegrationEvent>,
            SellerRegisteredRollUpHandler>();                      // service.merchant.v1

        services.AddScoped<
            IIntegrationEventHandler<UserRegisteredIntegrationEvent>,
            UserRegisteredRollUpHandler>();                        // service.identity.v1

        services.AddScoped<
            IIntegrationEventHandler<DriverCreatedIntegrationEvent>,
            DriverCreatedRollUpHandler>();                         // service.driver.v1

        // ── LOT 2 : ce que les champs optionnels ont debloque ────────────────
        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            OrderCancelledRollUpHandler>();                        // service.order.v1

        services.AddScoped<
            IIntegrationEventHandler<PaymentCapturedIntegrationEvent>,
            PaymentCapturedRollUpHandler>();                       // service.financial.v1

        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            PaymentFailedRollUpHandler>();                         // service.financial.v1

        return services;
    }
}
