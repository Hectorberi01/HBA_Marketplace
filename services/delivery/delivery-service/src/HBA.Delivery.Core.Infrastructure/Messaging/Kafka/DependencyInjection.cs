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

/// <summary>
/// LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// Les consommateurs de la plateforme vivaient dans SIX conventions differentes
/// selon le service. Chercher « qui ecoute quoi » supposait de connaître la
/// convention du service qu'on ouvrait.
///
/// LA LIGNE DE PARTAGE : CE DOSSIER PORTE LA POLITIQUE DU SERVICE, LE SOCLE
/// PARTAGE PORTE LE TYPE ET LE PROTOCOLE. `Outbox/` et `Inbox/` contiennent le
/// geste d'enregistrement, PAS une copie de `OutboxMessage` ni de
/// `ConsumerInboxEntry` — ce sont des entites EF dont les tables sont creees par
/// les migrations de ce service.
///
/// CE QUI N'EST PAS ICI :
/// `Serialization/` est absent : ce service n'a pas de convertisseur propre et
/// utilise celui de `HBA.Shared.Infrastructure.Kafka`. Un dossier vide se lirait
/// comme une promesse tenue ailleurs.
/// `Interceptors/` est absent : la correlation et le `traceparent` sont deja
/// portes par l'enveloppe partagee. Un intercepteur local serait une SECONDE
/// implementation du meme contrat.
///
/// L'IDEMPOTENCE reste dans l'installeur quand elle y est : elle sert aussi les
/// routes HTTP annotees `AllowIdempotency()`.
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
    public static IServiceCollection AjouterMessagerieDeliveryCore(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsDeliveryCore();
        services.AjouterOutboxDeliveryCore();
        services.AjouterInboxDeliveryCore();

        // ═════════════════════════════════════════════════════════════════════
        // SANS CETTE LIGNE, LA TABLE `deliveries.drivers` RESTE VIDE POUR
        //    TOUJOURS — ET ELLE L'ÉTAIT (lot 5.2).
        //
        // `IDriverRepository.AddAsync` n'avait aucun appelant : rien, nulle part,
        // ne créait de livreur dans ce module. Le dispatch lisait donc une table
        // que personne ne remplissait, et `RegisterDriverCommandHandler` — cité
        // par `DriverConfiguration` — n'a jamais existé.
        //
        // La ligne arrive désormais du DOSSIER tenu par driver-service, par
        // l'événement `driver.dossier-verified`. C'est la forme que D34 exige
        // entre deux propriétaires : un contrat ou un événement, jamais une
        // référence de projet vers le domaine du voisin.
        //
        // LA SUSPENSION EST BRANCHÉE DEPUIS, ET IL LE FALLAIT.
        //
        // `DriverSuspendedIntegrationEvent` était publié par driver-service et
        // personne ne l'écoutait : un livreur suspendu dans son dossier restait
        // dispatchable ici, et continuait d'aller chez les clients. Suspendre
        // quelqu'un et le laisser travailler, ce n'est pas une suspension.
        //
        // Reste une limite, écrite dans le gestionnaire : la course DÉJÀ EN COURS
        // n'est ni réaffectée ni annulée — c'est une décision d'exploitation, pas
        // une conséquence automatique. Le cas est journalisé en `Critical`.
        // ═════════════════════════════════════════════════════════════════════
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
