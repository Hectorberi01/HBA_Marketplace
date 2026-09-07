using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Producers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Merchants.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieMerchants(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsMerchants();
        services.AjouterOutboxMerchants();
        services.AjouterInboxMerchants();

        // LE DROIT À L'EFFACEMENT S'ARRÊTAIT À IDENTITY.
        services.AddScoped<
            IIntegrationEventHandler<UserAnonymizedIntegrationEvent>,
            UserAnonymizedSellerPurgeHandler>();

        // LES DEUX COMPTEURS DE LA VITRINE, QUI VALAIENT ZÉRO POUR TOUT LE MONDE.
        services.AddScoped<
            IIntegrationEventHandler<SellerRatingRecomputedIntegrationEvent>,
            SellerRatingHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            SellerSalesCountHandler>();

        return services;
    }
}
