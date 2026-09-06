using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.Configuration;
namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc;

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
    public static IServiceCollection AjouterClientsGrpcMarketplaceReturnRefund(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // ═════════════════════════════════════════════════════════════════════════
        // SANS CE CLIENT, LES ROUTES VENDEUR NE SAVENT PAS À QUI PARLE LE JETON.
        //
        // Les six routes de `/api/v1/seller/returns` exigeaient le rôle `Seller` et
        // rien d'autre : tout vendeur inscrit arbitrait — et CHIFFRAIT le
        // remboursement — du dossier d'un concurrent. Le vendeur d'un retour n'est
        // pas dans le jeton, il est dans la ressource ; seul seller-service sait
        // relier les deux.
        //
        // `AddMerchantsGrpcClient` LÈVE à la construction de l'hôte si
        // `Services:Merchant` est absent. C'est voulu, et c'est le bon sens de
        // l'erreur : un service d'arbitrage de remboursements qui démarre sans savoir
        // vérifier l'appartenance vaut moins qu'un service qui ne démarre pas.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddMerchantsGrpcClient(configuration);

        // LA VÉRIFICATION DES PREUVES PHOTO — SANS CE CLIENT, ELLE N'EXISTE PAS.
        //
        // `MediaGrpcClient` contactait auparavant personne : il vérifiait que
        // l'identifiant n'était pas vide, puis rendait succès. N'importe quel
        // identifiant inventé passait, et un dossier de retour se fermait sur une preuve
        // qui n'existe pas — ce qu'on ne découvre que le jour d'un litige.
        //
        // `AddMediaGrpcClient` lève si `Services:Media` est absent : le service ne
        // démarre pas plutôt que de valider des preuves sans les regarder.
        services.AddMediaGrpcClient(configuration);

        services.AddOrderingGrpcClient(configuration);

        services.AddFinancialGrpcClient(configuration);

        return services;
    }
}
