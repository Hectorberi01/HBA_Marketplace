using HBA.Identity.Contracts.Grpc;
using HBA.Inventory.Contracts.Grpc;
using HBA.Media.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Merchants.Infrastructure.Grpc.Configuration;
namespace HBA.Merchants.Infrastructure.Grpc;

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
    public static IServiceCollection AjouterClientsGrpcMerchants(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddIdentityGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // SANS CETTE LIGNE, N'IMPORTE QUEL MÉDIA DE LA PLATEFORME POUVAIT DEVENIR
        //    UNE PIÈCE KYB.
        //
        // `AddKybDocumentCommandHandler` vérifie désormais que le fichier appartient à
        // CE vendeur et qu'il est bien une pièce légale. Il lui faut donc `IMediaModuleApi`.
        //
        // Le service n'avait aucun client média : le domaine renvoyait le contrôle « à la
        // couche qui voit les deux », la documentation renvoyait ensuite au BFF Vendeur —
        // qui est un squelette sans aucun cas d'usage. La délégation ne pointait vers
        // personne, et un vendeur rattachait à son dossier la pièce d'identité d'un
        // concurrent avant de s'en faire signer l'URL.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddMediaGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // ET SANS CELLE-CI, UNE BOUTIQUE EXPÉDIAIT DEPUIS L'ADRESSE D'UN CONCURRENT.
        //
        // `AttachStoreLocationCommand` acceptait n'importe quel GUID. L'identifiant
        // partait ensuite vers delivery, qui bâtissait un enlèvement coursier sur une
        // adresse que le vendeur ne contrôle pas — et un GUID inexistant ne se
        // manifestait qu'APRÈS le paiement de l'acheteur.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddInventoryGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // POUR RECALCULER LE COMPTEUR DE VENTES, PAS POUR L'INCRÉMENTER.
        //
        // `SellerSalesCountHandler` redemande le total à order-service à chaque commande
        // confirmée : poser une valeur exacte est idempotent, incrémenter double-compte
        // au premier rejeu — et Kafka livre au moins une fois.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddOrderingGrpcClient(configuration);

        return services;
    }
}
