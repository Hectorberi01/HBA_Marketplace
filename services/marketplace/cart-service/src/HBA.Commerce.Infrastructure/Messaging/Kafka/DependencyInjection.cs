using HBA.Commerce.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Commerce.Infrastructure.Messaging.Kafka.Producers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Commerce.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieCommerce(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsCommerce();
        services.AjouterOutboxCommerce();
        services.AjouterInboxCommerce();

        // Chorégraphie : clôture du panier quand une commande est placée.
        services.AddScoped<
            IIntegrationEventHandler<OrderPlacedIntegrationEvent>,
            CloseCartOnOrderPlacedHandler>();

        return services;
    }
}
