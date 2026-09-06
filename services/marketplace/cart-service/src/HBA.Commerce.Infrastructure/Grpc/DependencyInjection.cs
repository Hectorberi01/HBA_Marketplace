using HBA.Commerce.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Commerce.Infrastructure.Grpc.Configuration;
namespace HBA.Commerce.Infrastructure.Grpc;

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
    public static IServiceCollection AjouterClientsGrpcCommerce(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddProductsGrpcClient(configuration);

        services.AddInventoryGrpcClient(configuration);

        services.AddOrderingGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════════
        // SANS CETTE LIGNE, AUCUNE CAMPAGNE COMMERCIALE N'EXISTE (ISSUE-033).
        //
        // La tarification neutre — qui fut le seul fournisseur du dépôt —
        // rendait des remises nulles et refusait tout coupon. promotion-service, complet
        // depuis son écriture, n'avait AUCUN appelant. C'est ce client qui lui en donne
        // un.
        //
        // `AddPromotionGrpcClient` LÈVE à la construction de l'hôte si `Services:Promotion`
        // est absent, et c'est le bon sens de l'erreur : un panier démarré sans savoir
        // joindre promotion refuserait silencieusement tous les coupons, et les clients
        // paieraient le plein tarif sans que rien ne le signale. Mieux vaut ne pas
        // démarrer.
        //
        // À NE PAS CONFONDRE AVEC LE REPLI D'EXÉCUTION.
        //
        // Une adresse ABSENTE est une erreur de déploiement : elle se corrige, et elle se
        // corrige mieux avant d'ouvrir le port. Un service INJOIGNABLE en cours de route
        // est un incident : là, `PromotionPricingModuleApi` valorise le panier sans remise
        // et le journalise, parce qu'une panne de promotion ne doit pas devenir une panne
        // de vente.
        // ═════════════════════════════════════════════════════════════════════════════
        services.AddPromotionGrpcClient(configuration);

        return services;
    }
}
