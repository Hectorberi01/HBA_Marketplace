using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Order.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Food.Order.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Food.Order.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Food.Order.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Food.Order.Infrastructure.Messaging.Kafka.Producers;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Food.Order.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieFoodOrder(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFoodOrder();
        services.AjouterOutboxFoodOrder();
        services.AjouterInboxFoodOrder();

        // ── Ce que la commande écoute : le paiement ─────────────────────────
        services.AddScoped<
            IIntegrationEventHandler<PaymentCapturedIntegrationEvent>,
            ConfirmMealOrderOnPaymentCapturedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            CancelMealOrderOnPaymentFailedHandler>();

        // ── Ce que la commande écoute : la cuisine ──────────────────────────
        //
        // Refus et annulation amènent au même endroit par deux chemins distincts
        // — voir `KitchenOutcomeHandlers`. La remise du repas est celle qui
        // manquait le plus : sans elle, une commande de repas ne se terminait
        // JAMAIS, l'escrow n'était pas levé, et le restaurateur n'était pas payé.
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>,
            CancelMealOrderOnKitchenRejectionHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>,
            CancelMealOrderOnKitchenCancellationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>,
            MarkMealOrderDeliveredOnKitchenDeliveryHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LA PORTE D'ENTRÉE DE L'ARBITRAGE, QUI N'EXISTAIT PAS (ISSUE-061).
        //
        // `PutMealOrderUnderReviewCommand`, son gestionnaire, `MarkUnderReview`
        // et ses quatre gardes, la colonne `ReviewReason` et son index partiel :
        // tout était écrit, et RIEN n'envoyait jamais cette commande. Les deux
        // routes d'administration qui SORTENT de l'arbitrage répondaient donc 409
        // à tous les coups. Voir `HoldMealOrderOnDeliveryCancelledHandler`.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>,
            HoldMealOrderOnDeliveryCancelledHandler>();

        return services;
    }
}
