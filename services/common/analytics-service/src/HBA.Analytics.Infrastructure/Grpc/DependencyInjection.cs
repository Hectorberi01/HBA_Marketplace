using HBA.Analytics.Infrastructure.Grpc.Clients;
using HBA.Analytics.Infrastructure.Grpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// LA LIGNE DE PARTAGE, LA MÊME QUE POUR KAFKA : CE DOSSIER PORTE LA POLITIQUE DU
///     SERVICE, LE SOCLE PARTAGÉ PORTE LE TYPE ET LE PROTOCOLE.
///
///   Configuration/  ce que CE service appelle, et avec quelle échéance
///   Clients/        les adaptateurs `I&lt;X&gt;ModuleApi` vers le stub gRPC
///
/// PAS DE `GardeDeCablage` ICI, CONTRAIREMENT À KAFKA. Un client gRPC oublié fait
/// échouer la résolution de son interface au démarrage, donc bruyamment. Un
/// module Kafka oublié produit un SILENCE — c'est cette dissymétrie qui justifie
/// une garde d'un côté et pas de l'autre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Branche les clients gRPC du service. Appelée par le composition root.
    /// </summary>
    public static IServiceCollection AjouterClientsGrpcAnalytics(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // ═════════════════════════════════════════════════════════════════════
        // SANS CE CLIENT, LE VENDEUR VOIT LES CHIFFRES D'UN AUTRE VENDEUR.
        //
        // La route est `GET /api/sellers/{sellerId}/analytics/sales` : le vendeur
        // est désigné par l'URL, le jeton porte un identifiant d'UTILISATEUR, et
        // rien dans ce service ne relie les deux. Seul seller-service le sait —
        // et lui seul sait aussi qu'un MEMBRE d'équipe, qui n'a pas de dossier
        // vendeur à son nom, agit pour le compte de son vendeur.
        //
        // C'est exactement la garde d'order-service sur `/api/sellers/{id}/orders`,
        // et c'est délibérément la même : deux résolutions différentes de
        // « ce compte, quel vendeur est-il ? » finiraient par répondre
        // différemment, et l'écart serait une fuite.
        //
        // LA TENTATION ÉTAIT DE S'EN PASSER. Ce service consomme
        // `SellerRegistered`, qui porte (SellerId, UserId) : il pourrait tenir sa
        // propre table de correspondance. Ce serait une SECONDE source de vérité
        // sur l'autorisation, aveugle aux membres d'équipe, aux révocations et
        // aux transferts de propriété — et elle ne se tromperait qu'au moment où
        // ça compte.
        //
        // `AddMerchantsGrpcClient` LÈVE à la construction de l'hôte si
        // `Services:Merchant` est absent. C'est le bon sens de l'erreur : un
        // service qui rend des chiffres de vente et démarre sans savoir vérifier
        // l'appartenance vaut moins qu'un service qui ne démarre pas.
        // ═════════════════════════════════════════════════════════════════════
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
