using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Producers;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Payments.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieFinancialPayments(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFinancialPayments();
        services.AjouterOutboxFinancialPayments();
        services.AjouterInboxFinancialPayments();

        // Chorégraphie : libération de l'escrow à la livraison de la commande.
        services.AddScoped<
            IIntegrationEventHandler<OrderDeliveredIntegrationEvent>,
            ReleaseEscrowOnOrderDeliveredHandler>();

        // LE MÊME GESTE POUR LE FOOD, QUI N'EXISTAIT PAS.
        services.AddScoped<
            IIntegrationEventHandler<MealOrderDeliveredIntegrationEvent>,
            ReleaseEscrowOnMealOrderDeliveredHandler>();

        // CE MAILLON MANQUAIT : PERSONNE NE REMBOURSAIT.
        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            RefundPaymentOnOrderCancelledHandler>();

        return services;
    }
}
