using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Producers;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Deliveries.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieDeliveryCore(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsDeliveryCore();
        services.AjouterOutboxDeliveryCore();
        services.AjouterInboxDeliveryCore();

        // SANS CETTE LIGNE, LA TABLE `deliveries.drivers` RESTE VIDE POUR TOUJOURS
        // — ET ELLE L'ÉTAIT (lot 5.2).
        services.AddScoped<
            IIntegrationEventHandler<DriverDossierVerifiedIntegrationEvent>,
            ProjectDriverOnDossierVerified>();

        services.AddScoped<
            IIntegrationEventHandler<DriverSuspendedIntegrationEvent>,
            WithdrawDriverOnDossierSuspended>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryCreatedIntegrationEvent>,
            WebhookOnDeliveryCreated>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryAcceptedIntegrationEvent>,
            WebhookOnDeliveryAccepted>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>,
            WebhookOnDeliveryPickedUp>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
            WebhookOnDeliveryCompleted>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>,
            WebhookOnDeliveryCancelled>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryNoDriverAvailableIntegrationEvent>,
            WebhookOnDeliveryNoDriver>();

        return services;
    }
}
