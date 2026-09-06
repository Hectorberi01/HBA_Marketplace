using HBA.Catalog.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Producers;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Catalog.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieCatalog(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsCatalog();
        services.AjouterOutboxCatalog();
        services.AjouterInboxCatalog();

        // Handlers d'events d'intégration venus du module Sellers : Catalog réagit au
        // cycle de vie du compte vendeur (fermeture -> dépublication, suppression ->
        // archivage des produits).
        services.AddScoped<
            IIntegrationEventHandler<SellerClosedIntegrationEvent>,
            SellerClosedProductInvalidationHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerDeletedIntegrationEvent>,
            SellerDeletedProductPurgeHandler>();

        // SANS CES DEUX LIGNES, SUSPENDRE UN VENDEUR NE RETIRE RIEN (ISSUE-025).
        //
        // Le répartiteur d'événements d'intégration résout PARESSEUSEMENT : un
        // événement sans gestionnaire enregistré ne provoque aucune erreur, aucun
        // avertissement. Il est marqué traité et disparaît. C'est exactement ce qui
        // arrivait à `SellerSuspendedIntegrationEvent` — publié depuis le premier
        // jour, y compris sur refus de dossier KYB, et consommé par personne d'autre
        // qu'une notification.
        services.AddScoped<
            IIntegrationEventHandler<SellerSuspendedIntegrationEvent>,
            SellerSuspendedOfferWithdrawalHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerSuspensionLiftedIntegrationEvent>,
            SellerSuspensionLiftedOfferReinstatementHandler>();

        // ET SANS CES TROIS-CI, FERMER UNE BOUTIQUE NE RETIRE RIEN (ISSUE-041).
        //
        // Même mécanique, un cran plus bas en granularité : seller-service publiait
        // les quatre événements du cycle de vie d'une boutique, catalog écoutait le
        // topic, et `SuspendStoreCatalogCommand` n'avait aucun appelant.
        //
        // Il n'y en a pas pour `StoreSuspensionLiftedIntegrationEvent` : lever la
        // sanction repasse la boutique en `Closed`, pas en `Open`. Les offres
        // doivent rester retirées jusqu'à ce que le vendeur rouvre.
        services.AddScoped<
            IIntegrationEventHandler<StoreClosedIntegrationEvent>,
            StoreClosedOfferWithdrawalHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StoreSuspendedIntegrationEvent>,
            StoreSuspendedOfferWithdrawalHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StoreOpenedIntegrationEvent>,
            StoreOpenedOfferReinstatementHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // ET SANS CES DEUX-CI, LE STOCK NE DÉCIDE DE RIEN (ISSUE-047).
        //
        // Aucune offre n'est jamais passée `OutOfStock`, ni n'est jamais revenue
        // en vente. `MarkOfferOutOfStockCommand` existait sans émetteur ;
        // `ListBySkuAsync` avait été écrite POUR ce cas — « Inventory s'en sert
        // pour signaler une rupture », dit son commentaire ; le contrat
        // d'inventaire annonçait « consommé par Offers ». Cinq fichiers
        // décrivaient un chemin que rien ne parcourait.
        //
        // Conséquence dans les deux sens : une offre en rupture restait
        // ACHETABLE — l'acheteur découvrait l'indisponibilité au checkout, après
        // avoir choisi son adresse — et un réassort ne remettait rien en vente.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<StockDepletedIntegrationEvent>,
            WithdrawOffersOnStockDepletedHandler>();

        services.AddScoped<
            IIntegrationEventHandler<StockReplenishedIntegrationEvent>,
            ReactivateOffersOnStockReplenishedHandler>();

        return services;
    }
}
