using HBA.Media.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Media.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Media.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Media.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Media.Infrastructure.Messaging.Kafka.Producers;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Media.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieMedia(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsMedia();
        services.AjouterOutboxMedia();
        services.AjouterInboxMedia();

        services.AddScoped<
            IIntegrationEventHandler<KybDocumentRemovedIntegrationEvent>,
            DeleteMediaOnKybDocumentRemovedHandler>();

        return services;
    }
}
