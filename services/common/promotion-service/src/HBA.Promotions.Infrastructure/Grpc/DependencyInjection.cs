using HBA.Promotions.Infrastructure.Grpc.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Promotions.Infrastructure.Grpc.Configuration;
namespace HBA.Promotions.Infrastructure.Grpc;

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
    public static IServiceCollection AjouterClientsGrpcPromotions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // ═════════════════════════════════════════════════════════════════════════════
        // SANS CE CLIENT, LES TROIS ROUTES MARCHAND NE SAVENT PAS À QUI PARLE LE JETON.
        //
        // Elles étaient fermées à `RequireAdmin` PAR DÉFAUT DE PROPRIÉTAIRE — c'était
        // écrit noir sur blanc dans `PromotionEndpoints`. D28 ajoute `OwnerSellerId` et
        // ouvre les routes au vendeur PROPRIÉTAIRE ; encore faut-il pouvoir répondre à
        // « ce compte, quel vendeur est-il ? ». Le jeton ne le dit pas, et un membre
        // d'équipe n'a pas de dossier vendeur à son nom : seul seller-service sait relier
        // les deux.
        //
        // `AddMerchantsGrpcClient` LÈVE à la construction de l'hôte si `Services:Merchant`
        // est absent. C'est le bon sens de l'erreur : un service qui pilote des budgets
        // promotionnels et démarre sans savoir vérifier l'appartenance vaut moins qu'un
        // service qui ne démarre pas.
        //
        // CE SERVICE N'APPELAIT PERSONNE, ET SON COMPOSE LE DISAIT.
        //
        // Le bloc `promotion-service` de `docker-compose.dev.yml` portait un encadré
        // « AUCUNE ADRESSE `SERVICES__*`, ET C'EST LE POINT FORT DE CE SERVICE ». Ce n'est
        // plus vrai, et l'encadré a été corrigé au lieu d'être laissé à mentir : la
        // dépendance est réelle, elle est d'AUTORISATION et non de calcul — promotion
        // continue d'ignorer ce qu'est un produit, un plat ou un restaurant.
        // ═════════════════════════════════════════════════════════════════════════════
        services.AddMerchantsGrpcClient(configuration);

        return services;
    }
}
