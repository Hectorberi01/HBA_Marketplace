using HBA.Deliveries.Contracts.Grpc;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.Inventory.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Food.Infrastructure.Grpc.Configuration;
namespace HBA.Food.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les 5 clients de ce service étaient
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
    public static IServiceCollection AjouterClientsGrpcFoodRestaurant(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // ═════════════════════════════════════════════════════════════════════════
        // LE PARCOURS FOOD, DE LA COMMANDE PAYÉE AU CLIENT SERVI.
        //
        // CES QUATRE PONTS N'EXISTAIENT PAS, ET LE PARCOURS S'ARRÊTAIT EN CHEMIN.
        //
        // Sans le premier, un client peut commander un repas, être débité, et aucune
        // cuisine n'est servie — le ticket n'est jamais ouvert. Sans le second, un
        // repas déclaré prêt refroidit sans qu'aucun livreur ne soit cherché.
        //
        // Les deux vivaient dans la composition root du monolithe. Le module Food est
        // parti dans son service ; les fichiers qui le reliaient au reste sont restés.
        //
        // LES DEUX DERNIERS FERMENT LA CHAÎNE — SANS EUX, PERSONNE N'EST PAYÉ.
        //
        // L'aller était branché, le RETOUR ne l'était pas : aucun service du dépôt ne
        // consommait la fin d'une course « FOOD- ». Le livreur remettait le repas au
        // client, le ticket restait « prêt » à vie, la commande restait « confirmée »,
        // et le gain du restaurateur restait bloqué en « à venir ».
        //
        // L'origine du défaut est une asymétrie : « ORDER- » et « FOOD- » ont été créés
        // dans le même geste, et seul « ORDER- » a été relu, chez order-service. Voir
        // `FoodDeliveryReturnHandlers` — le prochain préfixe posera le même piège.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddOrderingGrpcClient(configuration);

        services.AddInventoryGrpcClient(configuration);

        services.AddDeliveryGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // LE CINQUIÈME CLIENT — CELUI SANS LEQUEL AUCUN REPAS N'AVAIT DE LIVREUR.
        //
        // La création de course lisait l'adresse de remise chez order-service, sans se
        // demander d'où venait le ticket. Un ticket né d'une `MealOrder` porte un
        // identifiant que order-service ne connaît pas : le gestionnaire levait, les
        // reprises Kafka s'épuisaient, et le sac restait sur le passe. Muet côté
        // client — la commande reste « confirmée » indéfiniment.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddFoodOrdersGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // LE QUATRIÈME CLIENT — CELUI QUI REND LE RESTAURATEUR PAYABLE.
        //
        // `PUT /api/food/partner/restaurants/{id}/payout-seller` doit prouver que le
        // dossier vendeur rattaché appartient bien au porteur du jeton et qu'il est
        // actif. Le module Food ne peut pas le faire : sa frontière lui interdit de
        // connaître Sellers. La composition root, elle, le peut — c'est exactement ce
        // que dit `Restaurant.PayoutSellerId` : « vérifiés par la couche qui voit les
        // deux ».
        //
        // `AddMerchantsGrpcClient` LIT `Services:Merchant` ET LÈVE À LA CONSTRUCTION
        // DE L'HÔTE si l'adresse manque. Le conteneur sort avant la première requête,
        // et aucune sonde ne le dit. La ligne `Services__Merchant` de compose est donc
        // arrivée dans le même geste que cette ligne-ci.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
