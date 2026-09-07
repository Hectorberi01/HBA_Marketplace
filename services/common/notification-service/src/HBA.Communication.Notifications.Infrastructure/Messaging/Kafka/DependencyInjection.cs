using HBA.Communication.Contracts.IntegrationEvents;
using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Producers;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieCommunicationNotifications(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici coute un
        // demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsCommunicationNotifications();
        services.AjouterOutboxCommunicationNotifications();
        services.AjouterInboxCommunicationNotifications();

        // Les deux consommateurs qui n'existaient pas.
        services.AddScoped<
            IIntegrationEventHandler<EmailVerificationRequestedIntegrationEvent>,
            SendEmailVerificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<PasswordResetRequestedIntegrationEvent>,
            SendPasswordResetEmailHandler>();

        // QUATRIÈME MESSAGE PORTEUR D'UN SECRET, ET LE PREMIER QUI N'AVAIT MÊME PAS
        // D'ÉVÉNEMENT. Le code OTP était généré puis jeté (`_ = code;`) —
        // ISSUE-062.
        services.AddScoped<
            IIntegrationEventHandler<OtpChallengeIssuedIntegrationEvent>,
            SendOtpCodeHandler>();

        // TROISIÈME E-MAIL PORTEUR D'UN SECRET, ET MÊME RÈGLE QUE LES DEUX AUTRES :
        // sans consommateur, l'invitation part dans l'outbox, est marquée traitée,
        // et n'atteint personne — sans la moindre erreur.
        services.AddScoped<
            IIntegrationEventHandler<SellerMemberInvitedIntegrationEvent>,
            SendSellerInvitationEmailHandler>();

        // LA VIE D'UN MEMBRE D'ÉQUIPE — SEPT ÉVÉNEMENTS, ZÉRO CONSOMMATEUR (lot E).
        services.AddScoped<
            IIntegrationEventHandler<SellerMemberJoinedIntegrationEvent>,
            SellerMemberJoinedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberRolesUpdatedIntegrationEvent>,
            SellerMemberRolesUpdatedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberStoreAssignedIntegrationEvent>,
            SellerMemberStoreAssignedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberStoreUnassignedIntegrationEvent>,
            SellerMemberStoreUnassignedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberSuspendedIntegrationEvent>,
            SellerMemberSuspendedNotificationHandler>();

        // Le transfert de propriété (lot 7.2) : deux messages, un pour chaque
        // partie.
        services.AddScoped<
            IIntegrationEventHandler<SellerOwnershipTransferredIntegrationEvent>,
            SellerOwnershipTransferredNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberActivatedIntegrationEvent>,
            SellerMemberActivatedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberRevokedIntegrationEvent>,
            SellerMemberRevokedNotificationHandler>();

        // Consumers fan-out : un fait métier d'un autre module -> une notification.
        services.AddScoped<
            IIntegrationEventHandler<OrderPlacedIntegrationEvent>,
            OrderPlacedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            OrderConfirmedNotificationHandler>();

        // Le VENDEUR est prévenu de la même confirmation, par un handler distinct.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            SellerOrderConfirmedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            OrderCancelledNotificationHandler>();

        // COMMANDE EN ARBITRAGE, PUIS RELANCÉE.
        services.AddScoped<
            IIntegrationEventHandler<OrderUnderReviewIntegrationEvent>,
            OrderUnderReviewNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderResumedAfterReviewIntegrationEvent>,
            OrderResumedAfterReviewNotificationHandler>();

        // REMBOURSEMENTS. Deux moments, deux messages : - accepté : rassure
        // l'acheteur pendant que l'admin exécute le versement chez FedaPay (qui n'a
        // pas d'API de remboursement) ; - versé : l'argent est parti — on prévient
        // l'acheteur ET le vendeur, qui vient d'être débité.
        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundApprovedIntegrationEvent>,
            ReturnRefundApprovedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundedIntegrationEvent>,
            ReturnRefundedNotificationHandler>();

        // REMBOURSEMENT DE PAIEMENT — DISTINCT DES DEUX LIGNES CI-DESSUS.
        services.AddScoped<
            IIntegrationEventHandler<PaymentRefundedIntegrationEvent>,
            PaymentRefundedNotificationHandler>();

        // BOUTIQUE VALIDÉE. L'admin active le vendeur → e-mail de bienvenue +
        // push/in-app : le vendeur passe de « en attente » à « peut publier », il
        // doit le savoir tout de suite.
        services.AddScoped<
            IIntegrationEventHandler<SellerActivatedIntegrationEvent>,
            SellerActivatedNotificationHandler>();

        // REVERSEMENT VERSÉ. L'événement existait sans consommateur : le vendeur
        // était payé sans en être informé — le message qu'il attend le plus.
        services.AddScoped<
            IIntegrationEventHandler<PayoutPaidIntegrationEvent>,
            PayoutPaidNotificationHandler>();

        // COURSE PAYÉE. Même silence que ci-dessus, un étage plus bas : le livreur
        // n'était pas payé du tout, et une fois le crédit branché il l'aurait été
        // sans un mot.
        services.AddScoped<
            IIntegrationEventHandler<DriverEarningCreditedIntegrationEvent>,
            DriverEarningCreditedNotificationHandler>();

        // MESSAGERIE. Sans ça, un fil de discussion n'alerte personne : chaque
        // partie devait rouvrir l'application pour découvrir une réponse.
        services.AddScoped<
            IIntegrationEventHandler<MessageSentIntegrationEvent>,
            MessageSentNotificationHandler>();

        // CYCLE DE VIE BOUTIQUE : dossier KYB à valider (admin), suspension et
        // réactivation (vendeur, doublées par e-mail : il n'ouvrira pas forcément
        // l'app).
        services.AddScoped<
            IIntegrationEventHandler<SellerRegisteredIntegrationEvent>,
            SellerRegisteredAdminNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerClosedIntegrationEvent>,
            SellerClosedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerReactivatedIntegrationEvent>,
            SellerReactivatedNotificationHandler>();

        // TROIS NOTIFICATIONS QUI MANQUAIENT, TOUTES SUR DES DÉCISIONS SUBIES.
        services.AddScoped<
            IIntegrationEventHandler<SellerSuspendedIntegrationEvent>,
            SellerSuspendedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerSuspensionLiftedIntegrationEvent>,
            SellerSuspensionLiftedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerKybRejectedIntegrationEvent>,
            SellerKybRejectedNotificationHandler>();

        // ── HBA Food ─────────────────────────────────────────────────────────
        services.AddScoped<
            IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>,
            RestaurantApprovedNotificationHandler>();

        // LE SUIVI D'UN REPAS, CÔTÉ CLIENT.
        services.AddScoped<
            IIntegrationEventHandler<FoodOrderAcceptedIntegrationEvent>,
            FoodOrderAcceptedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderPreparingIntegrationEvent>,
            FoodOrderPreparingNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderReadyForPickupIntegrationEvent>,
            FoodOrderReadyNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<FoodOrderPickedUpIntegrationEvent>,
            FoodOrderPickedUpNotificationHandler>();

        // SANS CETTE LIGNE, UN LIVREUR A 45 SECONDES POUR ACCEPTER UNE COURSE DONT
        // RIEN NE L'AVERTIT.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryAssignedIntegrationEvent>,
            NotifyDriverOnDeliveryAssignedHandler>();

        // L'ACHETEUR NE SUIVAIT PAS SA LIVRAISON.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryAcceptedIntegrationEvent>,
            DeliveryAcceptedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>,
            DeliveryPickedUpNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<DeliveryNoDriverAvailableIntegrationEvent>,
            DeliveryNoDriverAlertHandler>();

        services.AddScoped<
            IIntegrationEventHandler<RestaurantRejectedIntegrationEvent>,
            RestaurantRejectedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<RestaurantSuspendedIntegrationEvent>,
            RestaurantSuspendedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<RestaurantReopenedIntegrationEvent>,
            RestaurantReopenedNotificationHandler>();

        // ACTIVITÉ VENDEUR : nouvel avis, rupture de stock (une vente perdue par
        // heure).
        services.AddScoped<
            IIntegrationEventHandler<ReviewPublishedIntegrationEvent>,
            ReviewPublishedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StockDepletedIntegrationEvent>,
            StockDepletedNotificationHandler>();

        // PAIEMENT ÉCHOUÉ : sans ce message, l'acheteur attend un colis qui ne
        // partira pas.
        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            PaymentFailedNotificationHandler>();

        // LES DECISIONS FAVORABLES, ET LES REFUS QUI RESTAIENT MUETS.
        services.AddScoped<
            IIntegrationEventHandler<SellerKybApprovedIntegrationEvent>,
            SellerKybApprovedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<ProductApprovedIntegrationEvent>,
            ProductApprovedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<ProductRejectedIntegrationEvent>,
            ProductRejectedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<ReviewRejectedIntegrationEvent>,
            ReviewRejectedNotificationHandler>();

        return services;
    }
}
