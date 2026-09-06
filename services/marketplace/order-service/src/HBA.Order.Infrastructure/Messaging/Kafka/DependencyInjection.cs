using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Orders.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Orders.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Orders.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Orders.Infrastructure.Messaging.Kafka.Producers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Orders.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieOrder(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsOrder();
        services.AjouterOutboxOrder();
        services.AjouterInboxOrder();

        // ═════════════════════════════════════════════════════════════════════════
        // LES DEUX SENS DE LA COURSE, ENFIN BRANCHÉS.
        //
        // `DeliveryCancelled` N'AVAIT QU'UN CONSOMMATEUR, INTERNE À DELIVERY.
        //
        // Le webhook partenaire. Rien ne remontait ici : une course annulée laissait la
        // commande `Confirmed` POUR TOUJOURS — payée, stock décrémenté, escrow gelé, et
        // un acheteur qui attend un colis que personne n'apportera.
        //
        // ET LA RÉCIPROQUE MANQUAIT AUSSI : une commande annulée laissait sa course
        // vivante, et un livreur partait chercher un colis que le vendeur ne remettrait
        // pas.
        //
        // Les deux se répondent, d'où le garde-fou anti-boucle documenté dans
        // `OrderDeliveryCancellation`.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>,
            HoldOrderOnDeliveryCancelledHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            CancelDeliveryOnOrderCancelledHandler>();

        // Suite du Saga : réactions aux résultats de paiement.
        services.AddScoped<
            IIntegrationEventHandler<PaymentCapturedIntegrationEvent>,
            ConfirmOrderOnPaymentCapturedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            CancelOrderOnPaymentFailedHandler>();

        // Étape finale du Saga : la course terminée clôt la commande (déclenche
        // escrow + payout vendeur).
        //
        // C'EST DELIVERY QUI L'ANNONCE, PLUS SHIPPING.
        //
        // Le module Shipping n'a pas été extrait du monolithe. L'ancien
        // gestionnaire réclamait `IShippingModuleApi`, que personne ne fournit :
        // la validation du conteneur refusait de démarrer le service. Voir
        // `MarkOrderDeliveredOnDeliveryCompletedHandler` pour ce que la bascule
        // coûte — le multi-colis.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
            MarkOrderDeliveredOnDeliveryCompletedHandler>();

        // LE RESTAURANT REFUSE → LA COMMANDE EST ANNULÉE.
        //
        // Sans ces deux lignes, le ticket passe « refusé » et la commande reste
        // « confirmée » : le client est débité pour un repas qui n'existera
        // jamais, et rien ne relie les deux faits.
        //
        // L'annulation publie `OrderCancelled` ; c'est financial-service qui
        // rembourse en la consommant. order-service annonce, il n'ordonne pas.
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>,
            CancelOrderOnFoodOrderRejectedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>,
            CancelOrderOnFoodOrderCancelledHandler>();

        // LE REPAS EST REMIS AU CLIENT → LA COMMANDE EST LIVRÉE.
        //
        // Sans cette ligne, une commande de repas ne se terminait JAMAIS : elle
        // restait « confirmée », `OrderDelivered` n'était jamais publié, l'escrow
        // n'était pas levé et le gain du restaurateur restait bloqué en « à
        // venir ». Le repas était remis au client et le restaurateur n'était
        // jamais payé.
        //
        // Le gestionnaire au-dessus, branché sur la fin de course, ne pouvait pas
        // s'en charger : il ne lit que « ORDER- », et le GUID d'une référence
        // « FOOD- » est celui du TICKET, inconnu de cette base. C'est food-service
        // qui traduit, en publiant `FoodOrderDelivered` avec l'`OrderId`.
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>,
            MarkOrderDeliveredOnFoodOrderDeliveredHandler>();

        // SANS CETTE LIGNE, LA COMMANDE N'APPREND JAMAIS QU'UN ARTICLE EST REVENU.
        //
        // `GetOrderReturnContextAsync` répondait `AlreadyReturnedQuantity: 0` et
        // `AlreadyRefundedAmount: 0m` en dur (ISSUE-014). Ce gestionnaire est la
        // seule source d'order-service sur les retours : non enregistré, il ne
        // manque rien au démarrage, aucune erreur n'apparaît, et le même
        // exemplaire se rembourse autant de fois qu'on ouvre de dossiers.
        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundedIntegrationEvent>,
            RecordReturnSettlementOnRefundHandler>();

        // ENREGISTREMENT INTROUVABLE DANS LE COMPOSITION ROOT.
        // Ce gestionnaire existe et n'etait enregistre nulle part : il n'a
        // jamais ete appele. Il l'est desormais.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            CreateDeliveryOnOrderConfirmedHandler>();

        return services;
    }
}
