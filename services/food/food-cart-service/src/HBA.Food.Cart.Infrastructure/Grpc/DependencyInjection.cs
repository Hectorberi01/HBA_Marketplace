using HBA.FoodCarts.Infrastructure.Grpc.Clients;
using HBA.FoodCarts.Infrastructure.Grpc.Clients;
using HBA.FoodCarts.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodCarts.Infrastructure.Grpc.Configuration;
namespace HBA.FoodCarts.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les 3 clients de ce service étaient
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
    public static IServiceCollection AjouterClientsGrpcFoodCart(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // La carte du restaurant : c'est elle qui donne le prix, et non le client.
        services.AddFoodGrpcClient(configuration);

        // La commande de repas : « cet acheteur en est-il à sa première ? », sans quoi
        // les promotions « première commande » seraient inapplicables.
        services.AddFoodOrdersGrpcClient(configuration);

        // LES PROMOTIONS — SANS CE CLIENT, LE PANIER DE REPAS N'A PAS DE TARIFICATION.
        //
        // `PromotionPricingModuleApi` prend un `IPromotionModuleApi` ; cette ligne est ce
        // qui le fournit. `AddPromotionGrpcClient` LÈVE à la construction de l'hôte si
        // `Services:Promotion` est absent — le service ne démarre pas, plutôt que de
        // refuser tout coupon en silence, ce qu'il faisait avant le 29 août 2026.
        services.AddPromotionGrpcClient(configuration);

        return services;
    }
}
