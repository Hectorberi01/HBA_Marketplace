using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsCommunicationNotifications
{
    private static readonly string[] Sujets =
    [
        // AJOUTE AVEC LES NOTIFICATIONS D'APPROBATION ET DE REFUS DE FICHE.
        // catalog-service publiait `ProductApproved` et `ProductRejected` sans que
        // personne les ecoute ; le vendeur ne savait pas que sa fiche etait passee.
        "service.catalog.v1",
        "service.communication.v1",
        "service.delivery.v1",
        "service.engagement.v1",
        "service.financial.v1",
        "service.food.v1",
        "service.identity.v1",
        "service.inventory.v1",
        "service.merchant.v1",
        "service.order.v1",
        "service.return-refund.v1"
    ];

    internal static IServiceCollection AjouterSujetsCommunicationNotifications(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
