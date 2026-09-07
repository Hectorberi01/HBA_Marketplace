using HBA.Analytics.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Analytics.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka;

/// <summary>
/// LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// LA LIGNE DE PARTAGE : CE DOSSIER PORTE LA POLITIQUE DU SERVICE, LE SOCLE
/// PARTAGE PORTE LE TYPE ET LE PROTOCOLE. `Inbox/` contient le geste
/// d'enregistrement, PAS une copie de `ConsumerInboxEntry` — c'est une entite EF
/// dont la table est creee par les migrations de ce service.
///
/// CE QUI N'EST PAS ICI, ET POURQUOI :
///
/// `Producers/` est absent : ce service ne publie AUCUN evenement. Un
/// `EvenementsPublies.cs` vide se lirait comme une liste qu'on aurait oublie de
/// remplir. Il n'y a donc pas non plus d'appel a `VerifierLesDescripteurs()` —
/// il n'y a rien a verifier.
///
/// `Outbox/`, `Processors/` et `Retry/` sont absents pour la meme raison : sans
/// evenement publie, il n'y a ni file a drainer, ni politique de reprise. Voir
/// l'encadre d'`AnalyticsDbContext`.
///
/// `Serialization/` est absent : ce service n'a pas de convertisseur propre et
/// utilise celui de `HBA.Shared.Infrastructure.Kafka`. Un dossier vide se lirait
/// comme une promesse tenue ailleurs.
///
/// La justification complete de cette forme est ecrite une seule fois, dans
/// `user-service` — `Messaging/Kafka/DependencyInjection.cs` et
/// `Messaging/Kafka/Outbox/OutboxUsers.cs`. Elle n'est pas recopiee ici.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Branche toute la messagerie du service. Appelee par `Program.cs` ; son
    /// absence est detectee au demarrage par `GardeDeCablage`.
    /// </summary>
    public static IServiceCollection AjouterMessagerieAnalytics(this IServiceCollection services)
    {
        services.AjouterSujetsAnalytics();
        services.AjouterInboxAnalytics();

        // LES TROIS GESTIONNAIRES, ET LEUR SUJET EN REGARD.
        //
        // Un gestionnaire enregistre ici dont le sujet manque dans
        // `SujetsAnalytics` ne sera JAMAIS appele, sans erreur ni journal. Les
        // deux listes se lisent donc ensemble, et c'est la seule chose qui les
        // relie.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            OrderConfirmedRollUpHandler>();                        // service.order.v1

        services.AddScoped<
            IIntegrationEventHandler<SellerRegisteredIntegrationEvent>,
            SellerRegisteredRollUpHandler>();                      // service.merchant.v1

        services.AddScoped<
            IIntegrationEventHandler<UserRegisteredIntegrationEvent>,
            UserRegisteredRollUpHandler>();                        // service.identity.v1

        return services;
    }
}
