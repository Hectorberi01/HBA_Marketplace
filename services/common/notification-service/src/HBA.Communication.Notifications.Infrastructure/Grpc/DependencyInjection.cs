using HBA.Deliveries.Contracts.Grpc;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.Identity.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Products.Contracts.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Communication.Notifications.Infrastructure.Grpc.Configuration;
namespace HBA.Communication.Notifications.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les 6 clients de ce service étaient
/// enregistrés dans `Program.cs`, entre le câblage HTTP et celui de la base.
/// Savoir « qui ce service appelle » supposait de lire un fichier de cent lignes
/// où trois préoccupations se mélangeaient. C'est désormais une liste, ici.
///
/// LA LIGNE DE PARTAGE, LA MÊME QUE POUR KAFKA : CE DOSSIER PORTE LA POLITIQUE DU
///     SERVICE, LE SOCLE PARTAGÉ PORTE LE TYPE ET LE PROTOCOLE.
///
///   Configuration/  ce que CE service appelle, et avec quelle échéance
///   Clients/        les adaptateurs — voir le LISEZMOI, ils ne sont pas encore ici
///   Mappers/        les traductions — même chose
///
/// CE QUE CE MODULE NE COUVRE PAS.
///
/// Il ne garantit pas d'être appelé. Contrairement à Kafka, où un module oublié
/// produit un SILENCE — le service démarre et n'écoute rien —, un client gRPC
/// oublié fait échouer la résolution de `I&lt;X&gt;ModuleApi` au démarrage, donc
/// bruyamment. C'est pour cette raison qu'il n'y a pas de `GardeDeCablage` ici :
/// elle vérifierait ce que le conteneur vérifie déjà.
///
/// LE CAS QUI ÉCHAPPE À CE RAISONNEMENT est l'hôte composé, où le même
/// `I&lt;X&gt;ModuleApi` peut être fourni à la fois par le module local du domaine
/// et par un client gRPC : le dernier enregistré gagne, en silence. Aucun service
/// n'est dans ce cas aujourd'hui — vérifié — et c'est la garde qui reste à écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Branche les clients gRPC du service. Appelée par le composition root.
    /// </summary>
    public static IServiceCollection AjouterClientsGrpcCommunicationNotifications(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // SANS CES TROIS LIGNES, LE MODULE DÉMARRE ET ÉCHOUE À LA PREMIÈRE
        //    NOTIFICATION — PAS AU DÉMARRAGE.
        //
        // Trois gestionnaires ont besoin de remonter d'un identifiant à un destinataire :
        // un avis porte un produit, une rupture porte un SKU, une inscription vendeur
        // n'indique aucun administrateur. Les interfaces sont résolues par le conteneur
        // à la CONSTRUCTION du gestionnaire, c'est-à-dire à la réception de l'événement.
        // Une dépendance manquante ne se voit donc pas au démarrage : elle se voit quand
        // une notification n'arrive pas.
        //
        // `AddProductsGrpcClient` pointe vers `Services:Catalog` — c'est catalog-service
        // qui héberge Products.
        services.AddProductsGrpcClient(configuration);

        services.AddMerchantsGrpcClient(configuration);

        services.AddIdentityGrpcClient(configuration);

        // CELUI-CI MANQUAIT, ET C'EST LE CONTENEUR QUI L'A DIT — AU DÉMARRAGE.
        //
        // Quatre gestionnaires réclament `IOrderingModuleApi` : le paiement refusé, les
        // deux étapes d'expédition, le cycle de vie vendeur. Tous doivent remonter d'un
        // identifiant de commande à son acheteur pour savoir À QUI écrire.
        //
        // Le commentaire ci-dessus annonçait qu'une dépendance manquante ne se verrait
        // qu'à la réception d'un événement. C'était vrai des gestionnaires construits à
        // la demande — mais `ValidateOnBuild` valide TOUS les descripteurs enregistrés
        // dès la construction du conteneur. Le service ne démarrait plus du tout.
        //
        // La validation au démarrage vaut mieux : une notification qui n'arrive pas ne
        // se remarque que le jour où quelqu'un s'en plaint.
        services.AddOrderingGrpcClient(configuration);

        // LE SECOND UNIVERS DE COMMANDES. Les quatre notifications de cuisine
        // résolvaient l'acheteur chez order-service seulement : le client d'une commande
        // de repas ne recevait donc AUCUN suivi, et l'échec ne produisait qu'un Warning.
        services.AddFoodOrdersGrpcClient(configuration);

        // POUR PRÉVENIR LE LIVREUR, IL FAUT REMONTER À SON COMPTE.
        //
        // `DeliveryAssignedIntegrationEvent` ne porte que le `DriverId` : il part aussi
        // vers l'API partenaires, à qui le compte HBA d'un livreur ne regarde pas. La
        // conversion se fait donc par lecture, pas par élargissement de l'événement.
        services.AddDeliveryGrpcClient(configuration);

        return services;
    }
}
