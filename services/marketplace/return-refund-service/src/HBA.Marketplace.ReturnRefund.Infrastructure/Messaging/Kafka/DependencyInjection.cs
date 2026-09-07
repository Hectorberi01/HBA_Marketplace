using HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Producers;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieMarketplaceReturnRefund(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsMarketplaceReturnRefund();
        services.AjouterOutboxMarketplaceReturnRefund();

        return services;
    }
}
