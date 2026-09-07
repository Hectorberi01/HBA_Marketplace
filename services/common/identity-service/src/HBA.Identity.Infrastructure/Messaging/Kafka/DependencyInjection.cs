using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Identity.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Identity.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Identity.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Identity.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Identity.Infrastructure.Messaging.Kafka.Producers;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Identity.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieIdentity(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsIdentity();
        services.AjouterOutboxIdentity();
        services.AjouterInboxIdentity();

        // RÔLES MÉTIER — TROIS ÉVÉNEMENTS VENUS D'AILLEURS.
        services.AddScoped<
            IIntegrationEventHandler<SellerRegisteredIntegrationEvent>,
            GrantSellerRoleHandler>();

        services.AddScoped<
            IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>,
            GrantFoodPartnerRoleHandler>();

        // `DriverVerifiedIntegrationEvent` DE `HBA.Drivers.Contracts`, PAS DE
        // `HBA.Deliveries.Contracts` — les deux le déclaraient, aux champs
        // identiques, et rendaient le même « driver.verified ».
        services.AddScoped<
            IIntegrationEventHandler<DriverVerifiedIntegrationEvent>,
            GrantDriverRoleHandler>();

        // LES DEUX LIGNES QUI RENDENT LE MODULE DES MEMBRES UTILISABLE.
        services.AddScoped<
            IIntegrationEventHandler<SellerMemberJoinedIntegrationEvent>,
            GrantSellerRoleToMemberHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberRevokedIntegrationEvent>,
            RevokeSellerRoleOnMemberRemovedHandler>();

        return services;
    }
}
