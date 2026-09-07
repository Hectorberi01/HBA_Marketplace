using HBA.Catalog.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Producers;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Catalog.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieCatalog(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsCatalog();
        services.AjouterOutboxCatalog();
        services.AjouterInboxCatalog();

        // Handlers d'events d'intégration venus du module Sellers : Catalog réagit
        // au cycle de vie du compte vendeur (fermeture -> dépublication,
        // suppression -> archivage des produits).
        services.AddScoped<
            IIntegrationEventHandler<SellerClosedIntegrationEvent>,
            SellerClosedProductInvalidationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerDeletedIntegrationEvent>,
            SellerDeletedProductPurgeHandler>();

        // SANS CES DEUX LIGNES, SUSPENDRE UN VENDEUR NE RETIRE RIEN (ISSUE-025).
        services.AddScoped<
            IIntegrationEventHandler<SellerSuspendedIntegrationEvent>,
            SellerSuspendedOfferWithdrawalHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerSuspensionLiftedIntegrationEvent>,
            SellerSuspensionLiftedOfferReinstatementHandler>();

        // ET SANS CES TROIS-CI, FERMER UNE BOUTIQUE NE RETIRE RIEN (ISSUE-041).
        services.AddScoped<
            IIntegrationEventHandler<StoreClosedIntegrationEvent>,
            StoreClosedOfferWithdrawalHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StoreSuspendedIntegrationEvent>,
            StoreSuspendedOfferWithdrawalHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StoreOpenedIntegrationEvent>,
            StoreOpenedOfferReinstatementHandler>();

        // ET SANS CES DEUX-CI, LE STOCK NE DÉCIDE DE RIEN (ISSUE-047).
        services.AddScoped<
            IIntegrationEventHandler<StockDepletedIntegrationEvent>,
            WithdrawOffersOnStockDepletedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StockReplenishedIntegrationEvent>,
            ReactivateOffersOnStockReplenishedHandler>();

        return services;
    }
}
