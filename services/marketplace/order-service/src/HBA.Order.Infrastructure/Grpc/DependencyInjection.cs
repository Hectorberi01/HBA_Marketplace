using HBA.Orders.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Orders.Infrastructure.Grpc.Configuration;
namespace HBA.Orders.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les 7 clients de ce service étaient
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
    public static IServiceCollection AjouterClientsGrpcOrder(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddInventoryGrpcClient(configuration);

        // LE CATALOGUE, POUR REVÉRIFIER LE PRIX AU CHECKOUT (ISSUE-048).
        //
        // Le prix, le statut « publié » et l'achetabilité d'une offre n'étaient jamais
        // relus entre l'ajout au panier et le paiement : tout venait du panier, qui fige
        // son prix à l'ajout. `Services:Catalog` est déjà fourni à ce service.
        services.AddProductsGrpcClient(configuration);

        // LE PANIER VIT DANS commerce-service, ET LE CHECKOUT EN DÉPEND.
        //
        // `PlaceOrderCommandHandler` lit le panier valorisé pour figer ses prix. Sans
        // ce client, `ICartModuleApi` n'a aucune implémentation et la validation du
        // conteneur refuse de démarrer le service — ce qu'elle a fait.
        services.AddCommerceGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // LA COURSE D'UNE COMMANDE MARKETPLACE — LE MAILLON QUI MANQUAIT.
        //
        // Shipping posait la référence dans le monolithe ; il n'a jamais été extrait.
        // Depuis, la chaîne s'arrêtait au paiement : aucune course, donc jamais
        // « livrée », donc aucun escrow libéré et AUCUN VENDEUR RÉGLÉ.
        //
        // Le gestionnaire vit dans le composition root : il connaît la commande, le
        // lieu d'expédition et le transporteur — trois mondes que la couche Application
        // n'a pas à connaître ensemble.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddDeliveryGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // LE DEVIS DE COURSE SE RELIT CHEZ delivery-pricing.
        //
        // `DeliveryApi.LookupQuote` n'a JAMAIS eu de corps de serveur : le checkout
        // rendait `UNIMPLEMENTED` sur toute commande portant un devis. Et
        // delivery-service n'a plus de domaine de tarification — l'implémenter chez lui
        // aurait interrogé une table vide. Ce client-ci apporte `IDeliveryQuoteLookup`.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddDeliveryPricingGrpcClient(configuration);

        // CE CLIENT NE SERT QU'À UNE AUTORISATION.
        //
        // `GET /api/sellers/{sellerId}/orders` doit vérifier que l'appelant EST ce
        // vendeur. Le jeton porte un identifiant d'utilisateur, la route un identifiant
        // de vendeur ; seule merchant-service connaît la correspondance. Sans lui, la
        // route rendait le carnet de commandes de n'importe quel vendeur à n'importe
        // quel inscrit.
        services.AddMerchantsGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // ORDER-SERVICE DOIT SAVOIR TRADUIRE « FOOD-… », ET SEULEMENT POUR CELA.
        //
        // Une course de repas annulée doit mettre la commande commerciale en arbitrage.
        // Sa référence porte l'identifiant du TICKET DE CUISINE, inconnu de cette base ;
        // `IFoodModuleApi.GetOrderAsync` fait la correspondance, et ne rend que des
        // rattachements — ni lignes, ni prix, ni ticket.
        //
        // Le chemin habituel (food-service traduit, puis republie) ne convenait pas ici :
        // le seul geste qu'il aurait sur son ticket, `CancelFoodOrderCommand`, publie
        // `FoodOrderCancelled` — que ce service consomme en ANNULANT la commande. Le
        // détour « propre » aurait donc produit exactement le remboursement automatique
        // qu'on refuse. Voir `HoldOrderOnDeliveryCancelledHandler`.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddFoodGrpcClient(configuration);

        return services;
    }
}
