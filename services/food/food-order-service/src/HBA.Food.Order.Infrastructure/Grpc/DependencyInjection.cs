using HBA.Deliveries.Contracts.Grpc;
using HBA.DeliveryPricing.Contracts.Grpc;
using HBA.Food.Contracts.Grpc;
using HBA.FoodCarts.Contracts.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodOrders.Infrastructure.Grpc.Configuration;
namespace HBA.FoodOrders.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les 4 clients de ce service étaient
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
    public static IServiceCollection AjouterClientsGrpcFoodOrder(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // LE PANIER VIT DANS food-cart-service, ET LE PASSAGE EN COMMANDE EN DÉPEND.
        //
        // Sans ce client, `IFoodCartModuleApi` n'a aucune implémentation et la
        // validation du conteneur refuse de démarrer le service. C'est exactement ce qui
        // est arrivé à order-service le jour où le panier a été extrait.
        services.AddFoodCartsGrpcClient(configuration);

        // La carte et l'appartenance au personnel : le restaurant prend-il encore des
        // commandes, et ce compte y travaille-t-il ?
        services.AddFoodGrpcClient(configuration);

        // LE DEVIS DE COURSE, RELU ET JAMAIS REDEMANDÉ.
        //
        // `RequestQuoteAsync` ÉCRIT et rendrait un SECOND prix, calculé sur la grille de
        // l'instant : on facturerait un montant que le client n'a jamais accepté. Seule
        // `LookupQuoteAsync` satisfait les deux exigences — le serveur impose le prix, ET
        // c'est le prix affiché.
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

        return services;
    }
}
