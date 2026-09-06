using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Producers;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Payments.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieFinancialPayments(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFinancialPayments();
        services.AjouterOutboxFinancialPayments();
        services.AjouterInboxFinancialPayments();

        // Chorégraphie : libération de l'escrow à la livraison de la commande.
        services.AddScoped<
            IIntegrationEventHandler<OrderDeliveredIntegrationEvent>,
            ReleaseEscrowOnOrderDeliveredHandler>();

        // LE MÊME GESTE POUR LE FOOD, QUI N'EXISTAIT PAS.
        //
        // `MealOrderDeliveredIntegrationEvent` était publié sans aucun consommateur.
        // Invisible tant qu'aucun repas ne pouvait être payé ; impasse dès que le
        // lot 6.1 ouvre ce chemin — client débité, restaurateur jamais reversable.
        services.AddScoped<
            IIntegrationEventHandler<MealOrderDeliveredIntegrationEvent>,
            ReleaseEscrowOnMealOrderDeliveredHandler>();

        // CE MAILLON MANQUAIT : PERSONNE NE REMBOURSAIT.
        //
        // `OrderCancelled` avait deux consommateurs — la reprise des gains
        // vendeur et la notification au client. Aucun ne rendait l'argent. Le
        // monolithe le faisait dans un helper de sa composition root, qui avait
        // accès aux deux modules à la fois ; le geste s'est perdu à la découpe.
        //
        // C'est financial qui possède le paiement : c'est donc à lui de décider
        // ce qu'annuler implique.
        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            RefundPaymentOnOrderCancelledHandler>();

        return services;
    }
}
