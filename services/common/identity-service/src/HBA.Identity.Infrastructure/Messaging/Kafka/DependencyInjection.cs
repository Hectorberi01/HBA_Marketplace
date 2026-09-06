using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Identity.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Identity.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Identity.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Identity.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Identity.Infrastructure.Messaging.Kafka.Producers;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Identity.Infrastructure.Messaging.Kafka;

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
    public static IServiceCollection AjouterMessagerieIdentity(this IServiceCollection services)
    {
        // Ce qu'on publie est verifie AVANT ce qu'on ecoute : echouer ici
        // coute un demarrage, le decouvrir en face coute des semaines.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsIdentity();
        services.AjouterOutboxIdentity();
        services.AjouterInboxIdentity();

        // ═════════════════════════════════════════════════════════════════
        // RÔLES MÉTIER — TROIS ÉVÉNEMENTS VENUS D'AILLEURS.
        //
        // Sans ces trois lignes, les rôles Seller, FoodPartner et Driver ne sont
        // attribués par personne : ils existent en base, semés au démarrage, et
        // aucun compte ne les porte. Les BFF partenaire et livreur répondent
        // alors 403 à tout le monde, sans qu'aucun journal ne relie le refus à
        // l'inscription qui aurait dû donner le droit.
        // ═════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<SellerRegisteredIntegrationEvent>,
            GrantSellerRoleHandler>();

        services.AddScoped<
            IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>,
            GrantFoodPartnerRoleHandler>();

        // `DriverVerifiedIntegrationEvent` DE `HBA.Drivers.Contracts`, PAS DE
        //    `HBA.Deliveries.Contracts` — les deux le déclaraient, aux champs
        //    identiques, et rendaient le même « driver.verified ». Le consommateur
        //    ne voyait que le type retenu par ordre alphabétique : enregistré sur
        //    l'autre, ce handler n'aurait JAMAIS été appelé, sans erreur, et le rôle
        //    `Driver` ne serait attribué à personne. La déclaration côté Deliveries
        //    a été retirée : l'agrégat décrit est le livreur, pas la course.
        services.AddScoped<
            IIntegrationEventHandler<DriverVerifiedIntegrationEvent>,
            GrantDriverRoleHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX LIGNES QUI RENDENT LE MODULE DES MEMBRES UTILISABLE.
        //
        // `MapSellerGroup` ne regarde que la claim de rôle du jeton. Sans le
        // premier consommateur, un membre correctement écrit en base est refoulé
        // par le ROUTAGE, avant tout handler et avant toute permission — et rien,
        // ni côté merchant ni ici, ne le signale.
        //
        // Le second est asymétrique par nature : il ne retire le rôle que si
        // seller-service a établi qu'il ne reste AUCUNE autre appartenance. Voir
        // l'encadré du handler.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<
            IIntegrationEventHandler<SellerMemberJoinedIntegrationEvent>,
            GrantSellerRoleToMemberHandler>();

        services.AddScoped<
            IIntegrationEventHandler<SellerMemberRevokedIntegrationEvent>,
            RevokeSellerRoleOnMemberRemovedHandler>();

        return services;
    }
}
