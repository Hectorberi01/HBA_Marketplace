using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Producers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieFinancialWallet(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsFinancialWallet();
        services.AjouterOutboxFinancialWallet();
        services.AjouterInboxFinancialWallet();

        // Chorégraphie : alimentation du grand livre des gains à la confirmation de commande.
        //
        // CE HANDLER EXIGE `ICommissionModuleApi`, ENREGISTRÉ PAR BILLING.
        //
        // Le taux prélevé ne vient plus de `PricingOptions` mais du moteur de
        // règles. Les deux modules vivent dans le même service et le même
        // conteneur (voir HBA.Financial.Api/Program.cs, qui installe les deux) :
        // l'appel est en processus, sans réseau.
        //
        // Installer Settlement SANS Billing ferait échouer la résolution de ce
        // handler au premier message — et non au démarrage. Si les deux modules
        // devaient un jour être séparés, c'est ici que la dépendance se voit.
        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            AccrueEarningsOnOrderConfirmedHandler>();

        // CONTRE-PASSATION. Sans ce handler, l'événement « retour remboursé » était
        // publié dans le vide : le vendeur gardait son gain sur un article qui nous
        // revenait, et la plateforme payait deux fois — le client ET le vendeur.
        services.AddScoped<
            IIntegrationEventHandler<ReturnRefundedIntegrationEvent>,
            ReverseEarningsOnReturnRefundedHandler>();

        // SANS CELUI-CI, UN REPAS REFUSÉ LAISSE SON GAIN AU GRAND LIVRE.
        //
        // La restauration comptabilise à la CONFIRMATION, puis le restaurant peut
        // refuser. Ce refus rembourse le client sans passer par un retour : rien
        // n'écoutait, et le solde à venir du restaurateur restait gonflé pour un
        // repas jamais servi, commission et frais encaissés compris.
        services.AddScoped<
            IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            ReverseEarningsOnOrderCancelledHandler>();

        // Libération des gains (escrow levé) à la livraison confirmée → payables.
        services.AddScoped<
            IIntegrationEventHandler<OrderDeliveredIntegrationEvent>,
            ReleaseEarningsOnOrderDeliveredHandler>();

        // SANS CETTE LIGNE, LE LIVREUR N'EST JAMAIS PAYÉ.
        //
        // Tout existait sauf le fil : le gain était calculé à la remise, porté par
        // `DeliveryCompletedIntegrationEvent`, et `CreditDriverEarningCommand`
        // savait créditer — mais personne ne l'appelait. Cette liste enregistrait
        // cinq événements de commande, d'expédition et de retour, et pas la fin de
        // course. Le portefeuille du livreur restait à zéro à vie, et l'écran
        // « Revenus » de son application lisait un solde que rien ne faisait bouger.
        services.AddScoped<
            IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
            CreditDriverOnDeliveryCompletedHandler>();

        return services;
    }
}
