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
using HBA.Shipping.Contracts.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieCommunicationNotifications(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsCommunicationNotifications();
        services.AjouterOutboxCommunicationNotifications();
        services.AjouterInboxCommunicationNotifications();

        // Les deux consommateurs qui n'existaient pas. Sans eux, les événements étaient
        // publiés, marqués traités, et disparaissaient — sans la moindre erreur.
        services.AddScoped<
            IIntegrationEventHandler<EmailVerificationRequestedIntegrationEvent>,
            SendEmailVerificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<PasswordResetRequestedIntegrationEvent>,
            SendPasswordResetEmailHandler>();

        // QUATRIÈME MESSAGE PORTEUR D'UN SECRET, ET LE PREMIER QUI N'AVAIT MÊME PAS
        // D'ÉVÉNEMENT. Le code OTP était généré puis jeté (`_ = code;`) — ISSUE-062.
        services.AddScoped<
            IIntegrationEventHandler<OtpChallengeIssuedIntegrationEvent>,
            SendOtpCodeHandler>();

        // TROISIÈME E-MAIL PORTEUR D'UN SECRET, ET MÊME RÈGLE QUE LES DEUX
        // AUTRES : sans consommateur, l'invitation part dans l'outbox, est marquée
        // traitée, et n'atteint personne — sans la moindre erreur.
        services.AddScoped<
            IIntegrationEventHandler<SellerMemberInvitedIntegrationEvent>,
            SendSellerInvitationEmailHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LA VIE D'UN MEMBRE D'ÉQUIPE — SEPT ÉVÉNEMENTS, ZÉRO CONSOMMATEUR (lot E).
        //
        // MÊME TROU QUE LES TROIS LIGNES CI-DESSUS, EN SEPT EXEMPLAIRES.
        //
        // Rejoindre, changer de rôle, être affecté à une boutique, en être retiré,
        // être suspendu, réactivé, révoqué : tout partait dans l'outbox, était
        // marqué traité, et disparaissait sans erreur. L'employé rétrogradé
        // découvrait sa rétrogradation en se cognant à un 403, et appelait un
        // gérant qui avait oublié l'avoir fait.
        //
        // ET C'EST DEVENU PLUS URGENT AVEC LE CADRAGE PAR BOUTIQUE (lot F).
        //
        // Tant qu'un rôle de boutique s'appliquait au vendeur entier, le retrait
        // d'une affectation ne changeait rien de visible. Il retire désormais
        // RÉELLEMENT des droits : le taire produirait des refus sur un magasin où
        // l'employé travaillait la veille.
        // ═════════════════════════════════════════════════════════════════════
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

        // Le transfert de propriété (lot 7.2) : deux messages, un pour chaque partie.
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
        // Le dispatcher résout tous les handlers d'un event (GetServices) : celui de
        // l'acheteur et celui du vendeur coexistent sans se gêner. Séparés, parce
        // qu'ils ne disent pas la même chose à des gens qui n'attendent pas la même
        // chose — et que l'un peut échouer sans faire tomber l'autre.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            SellerOrderConfirmedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            OrderCancelledNotificationHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // COMMANDE EN ARBITRAGE, PUIS RELANCÉE.
        //
        // SANS CES DEUX LIGNES, L'ACHETEUR NE SAIT TOUJOURS RIEN.
        //
        // Une commande devenue inexécutable — course annulée, expédition
        // multi-lieux — restait « confirmée » sans un mot : le client attendait
        // un colis que personne n'apportait, argent encaissé et stock décrémenté,
        // et découvrait le problème plusieurs jours plus tard en appelant.
        //
        // CE N'EST PAS UNE ANNULATION, ET LE MESSAGE NE DOIT PAS LE LAISSER
        // CROIRE. Une course annulée se réattribue le plus souvent ; annoncer un
        // échec ferait exiger un remboursement à quelqu'un qui recevra son colis
        // le lendemain.
        //
        // La reprise est notifiée elle aussi : « nous vous recontactons très
        // vite » suivi de rien vaut moins que le silence.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<OrderUnderReviewIntegrationEvent>,
            OrderUnderReviewNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderResumedAfterReviewIntegrationEvent>,
            OrderResumedAfterReviewNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<ShipmentShippedIntegrationEvent>,
            ShipmentShippedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>,
            ShipmentDeliveredNotificationHandler>();

        // REMBOURSEMENTS. Deux moments, deux messages :
        //  - accepté : rassure l'acheteur pendant que l'admin exécute le versement
        //              chez FedaPay (qui n'a pas d'API de remboursement) ;
        //  - versé   : l'argent est parti — on prévient l'acheteur ET le vendeur,
        //              qui vient d'être débité.
        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundApprovedIntegrationEvent>,
            ReturnRefundApprovedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundedIntegrationEvent>,
            ReturnRefundedNotificationHandler>();

        // REMBOURSEMENT DE PAIEMENT — DISTINCT DES DEUX LIGNES CI-DESSUS.
        //
        // Celles-ci suivent un RETOUR de marchandise. `PaymentRefunded` suit
        // l'annulation d'une commande avant expédition, et n'avait AUCUN
        // consommateur : l'acheteur était remboursé sans jamais l'apprendre.
        services.AddScoped<
            IIntegrationEventHandler<PaymentRefundedIntegrationEvent>,
            PaymentRefundedNotificationHandler>();

        // BOUTIQUE VALIDÉE. L'admin active le vendeur → e-mail de bienvenue + push/in-app :
        // le vendeur passe de « en attente » à « peut publier », il doit le savoir tout de suite.
        services.AddScoped<
            IIntegrationEventHandler<SellerActivatedIntegrationEvent>,
            SellerActivatedNotificationHandler>();

        // REVERSEMENT VERSÉ. L'événement existait sans consommateur : le vendeur était
        // payé sans en être informé — le message qu'il attend le plus.
        services.AddScoped<
            IIntegrationEventHandler<PayoutPaidIntegrationEvent>,
            PayoutPaidNotificationHandler>();

        // COURSE PAYÉE. Même silence que ci-dessus, un étage plus bas : le livreur
        // n'était pas payé du tout, et une fois le crédit branché il l'aurait été
        // sans un mot. Il aurait dû ouvrir l'écran « Revenus » et comparer deux
        // chiffres de mémoire pour deviner qu'une course lui avait été réglée.
        services.AddScoped<
            IIntegrationEventHandler<DriverEarningCreditedIntegrationEvent>,
            DriverEarningCreditedNotificationHandler>();

        // MESSAGERIE. Sans ça, un fil de discussion n'alerte personne : chaque partie
        // devait rouvrir l'application pour découvrir une réponse.
        // LES DEUX GESTIONNAIRES DE LITIGE MANQUENT, ET C'EST DÉLIBÉRÉ.
        //
        // Le module Disputes n'est pas encore extrait : ses événements
        // d'intégration n'existent nulle part côté HBA. S'y abonner créerait un
        // abonnement Kafka sans producteur — un module qui démarre, compile, et
        // ne notifie jamais rien. Voir
        // Notifications/EventHandlers/_LITIGES_A_REPRENDRE.md.
        services.AddScoped<
            IIntegrationEventHandler<MessageSentIntegrationEvent>,
            MessageSentNotificationHandler>();

        // CYCLE DE VIE BOUTIQUE : dossier KYB à valider (admin), suspension et
        // réactivation (vendeur, doublées par e-mail : il n'ouvrira pas forcément l'app).
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
        //
        // Une suspension retire tout le catalogue de la vente, un refus de dossier
        // bloque l'activation : le vendeur les découvrait par la chute de ses
        // commandes ou par un statut sans explication. Ce sont précisément les
        // moments où il doit être prévenu, et avec le motif.
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
        //
        // AUCUNE DE CES QUATRE N'EXISTAIT. Le module Food levait ses événements
        // et rien ne les publiait : le restaurateur ne savait ni que son dossier
        // était refusé, ni pourquoi son établissement avait disparu de
        // l'application. Même défaut que côté vendeurs, reproduit trois messages
        // plus tard.
        services.AddScoped<
            IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>,
            RestaurantApprovedNotificationHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LE SUIVI D'UN REPAS, CÔTÉ CLIENT.
        //
        // QUATRE ÉTAPES SUR SEPT, ET LE RESTE EST DÉLIBÉRÉ.
        //
        // `FoodOrderReceived` arrive dans la même seconde que « commande
        // confirmée » : un second message n'apprend rien. `Rejected` et
        // `Cancelled` annulent la commande, et `OrderCancelled` est déjà notifié
        // plus haut — deux messages pour un fait finiraient par se contredire.
        //
        // Un fait, une notification, chez celui qui possède le fait.
        // ═════════════════════════════════════════════════════════════════════
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

        // SANS CETTE LIGNE, UN LIVREUR A 45 SECONDES POUR ACCEPTER UNE COURSE
        //    DONT RIEN NE L'AVERTIT.
        //
        // Le dispatch choisit, démarre le chronomètre, et l'expiration tombe sans
        // que l'intéressé ait rien su. La course repart au suivant, puis finit en
        // « aucun livreur disponible » — sur une plateforme où des livreurs sont
        // pourtant disponibles.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryAssignedIntegrationEvent>,
            NotifyDriverOnDeliveryAssignedHandler>();

        // L'ACHETEUR NE SUIVAIT PAS SA LIVRAISON.
        //
        // communication-service ne consommait AUCUN événement de course. Le
        // client payait, puis n'entendait plus parler de sa commande jusqu'à ce
        // qu'un livreur sonne — alors que c'est l'information qu'il regarde le
        // plus, et la première raison d'appeler le support.
        //
        // Pas de notification sur `DeliveryCompleted` : la remise fait passer la
        // commande à « livrée », qui publie `OrderDelivered`, déjà notifié.
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

        // ACTIVITÉ VENDEUR : nouvel avis, rupture de stock (une vente perdue par heure).
        services.AddScoped<
            IIntegrationEventHandler<ReviewPublishedIntegrationEvent>,
            ReviewPublishedNotificationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StockDepletedIntegrationEvent>,
            StockDepletedNotificationHandler>();

        // PAIEMENT ÉCHOUÉ : sans ce message, l'acheteur attend un colis qui ne partira pas.
        services.AddScoped<
            IIntegrationEventHandler<PaymentFailedIntegrationEvent>,
            PaymentFailedNotificationHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LES DECISIONS FAVORABLES, ET LES REFUS QUI RESTAIENT MUETS.
        //
        // Quatre evenements etaient publies sans consommateur, alors que leur
        // symetrique negatif — ou positif — etait deja notifie. Voir
        // `ApprobationsEtRefusHandlers` pour le motif complet.
        // ═════════════════════════════════════════════════════════════════════
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
