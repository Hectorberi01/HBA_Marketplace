using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Producers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieFinancialWallet(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFinancialWallet();
        services.AjouterOutboxFinancialWallet();
        services.AjouterInboxFinancialWallet();

        // Chorégraphie : alimentation du grand livre des gains à la confirmation de
        // commande.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            AccrueEarningsOnOrderConfirmedHandler>();

        // CONTRE-PASSATION. Sans ce handler, l'événement « retour remboursé » était
        // publié dans le vide : le vendeur gardait son gain sur un article qui nous
        // revenait, et la plateforme payait deux fois — le client ET le vendeur.
        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundedIntegrationEvent>,
            ReverseEarningsOnReturnRefundedHandler>();

        // SANS CELUI-CI, UN REPAS REFUSÉ LAISSE SON GAIN AU GRAND LIVRE.
        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            ReverseEarningsOnOrderCancelledHandler>();

        // Libération des gains (escrow levé) à la livraison confirmée → payables.
        services.AddScoped<
            IIntegrationEventHandler<OrderDeliveredIntegrationEvent>,
            ReleaseEarningsOnOrderDeliveredHandler>();

        // SANS CETTE LIGNE, LE LIVREUR N'EST JAMAIS PAYÉ.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
            CreditDriverOnDeliveryCompletedHandler>();

        return services;
    }
}
