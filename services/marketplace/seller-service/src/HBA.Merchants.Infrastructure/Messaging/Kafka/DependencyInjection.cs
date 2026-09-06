using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Producers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Merchants.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieMerchants(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsMerchants();
        services.AjouterOutboxMerchants();
        services.AjouterInboxMerchants();

        // ═════════════════════════════════════════════════════════════════════
        // LE DROIT À L'EFFACEMENT S'ARRÊTAIT À IDENTITY.
        //
        // `UserAnonymizedIntegrationEvent` n'était consommé que par user-service.
        // seller-service détient pourtant ce que la plateforme a de plus sensible :
        // cartes d'identité, registres de commerce, documents fiscaux. Sans ce
        // consommateur, ils survivaient à l'effacement du compte — sans plus rien
        // pour les relier à une personne, donc sans moyen de les retrouver.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<UserAnonymizedIntegrationEvent>,
            UserAnonymizedSellerPurgeHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX COMPTEURS DE LA VITRINE, QUI VALAIENT ZÉRO POUR TOUT LE MONDE.
        //
        // `Rating` et `SalesCount` étaient persistés, projetés, affichés — et
        // n'avaient AUCUN alimenteur : `Seller.UpdateRating` n'avait pas un seul
        // appelant dans le dépôt, et rien n'incrémentait `SalesCount`. Un vendeur
        // ayant écoulé trois cents commandes était présenté comme n'ayant jamais
        // rien vendu.
        //
        // Les deux gestionnaires POSENT une valeur recalculée depuis la source, ils
        // n'accumulent pas : c'est ce qui les rend idempotents face à un rejeu, et
        // c'est la règle que `Seller.SetSalesCount` écrit lui-même.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<SellerRatingRecomputedIntegrationEvent>,
            SellerRatingHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            SellerSalesCountHandler>();

        return services;
    }
}
