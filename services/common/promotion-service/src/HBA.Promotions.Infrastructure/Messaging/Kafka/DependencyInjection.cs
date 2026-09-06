using HBA.Promotions.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Producers;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Promotions.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessageriePromotions(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsPromotions();
        services.AjouterOutboxPromotions();
        services.AjouterInboxPromotions();

        // ═════════════════════════════════════════════════════════════════════════════
        // LES DEUX COMPENSATIONS DU §10.16.
        //
        // INSCRITES DANS LE COMPOSITION ROOT, PAS DANS L'INSTALLEUR DU MODULE.
        //
        // `PromotionsModuleInstaller` vit dans Infrastructure, à qui l'on interdit de
        // connaître les contrats d'un autre service. C'est cette frontière qui permet à
        // la persistance de promotion d'ignorer ce qu'est une cuisine ou un panier
        // marketplace, et de rester redéployable seule.
        //
        // SANS CES DEUX LIGNES, LE BUDGET NE REVIENT JAMAIS.
        //
        // L'annulation d'une commande payée laisserait la remise engagée : la campagne se
        // viderait sur des commandes qui n'existent plus, et le client resterait bloqué
        // sur son plafond pour un achat qu'il n'a jamais reçu. Rien ne le signalerait —
        // un événement sans destinataire ne se plaint pas. C'est exactement ainsi que le
        // pont identity → user avait été perdu à l'extraction.
        // ═════════════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            ReleaseCouponsOnOrderCancelledHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>,
            ReleaseCouponsOnFoodOrderCancelledHandler>();

        return services;
    }
}
