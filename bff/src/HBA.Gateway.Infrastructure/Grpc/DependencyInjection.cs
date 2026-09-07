using HBA.Gateway.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Gateway.Infrastructure.Grpc.Configuration;
namespace HBA.Gateway.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les 1 clients de ce service étaient
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
    public static IServiceCollection AjouterClientsGrpcGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // LÈVE À LA CONSTRUCTION DE L'HÔTE si `Services:Identity` est absente.
        // C'est déjà le cas sans ce fichier : `ServicesOptions.Identity` porte
        // `[Required, Url]` et la validation est vérifiée au démarrage.
        services.AddIdentityGrpcClient(configuration);

        return services;
    }
}
