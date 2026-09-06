using HBA.Media.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Catalog.Infrastructure.Grpc.Configuration;
namespace HBA.Catalog.Infrastructure.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLIENTS gRPC DE CE SERVICE — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les 2 clients de ce service étaient
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
    public static IServiceCollection AjouterClientsGrpcCatalog(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        // ═════════════════════════════════════════════════════════════════════════
        // UNE SEULE LIGNE MANQUAIT POUR FERMER UN IDOR OUVERT DEPUIS L'ORIGINE.
        //
        // `CatalogEndpoints` affirmait que la propriété était invérifiable parce que
        // « catalog-service ne référence pas HBA.Merchants.Contracts ». C'était faux :
        // `HBA.Catalog.Application` le référence depuis toujours (pour les événements
        // d'intégration du cycle de vie vendeur), le `COPY` est dans le Dockerfile et
        // `SERVICES__MERCHANT` est dans le compose.
        //
        // Il ne manquait que l'ENREGISTREMENT du client. Le commentaire décrivait une
        // impossibilité là où il y avait un oubli — et c'est ce qui l'a laissé vivre.
        // ═════════════════════════════════════════════════════════════════════════
        services.AddMerchantsGrpcClient(configuration);

        // ═════════════════════════════════════════════════════════════════════════
        // LE MÊME OUBLI SE REJOUAIT AVEC LE MÉDIA.
        //
        // `AddProductMediaCommandHandler` dépend maintenant de `IMediaModuleApi` pour
        // vérifier qu'une image appartient bien au produit avant de l'afficher. Cette
        // dépendance ne se voit qu'à l'exécution : sans cette ligne, le conteneur
        // démarre, la vitrine fonctionne, et SEUL le rattachement d'image casse — avec
        // une 500 que rien ne relie à une configuration manquante.
        //
        // `SERVICES__MEDIA` est déjà posé par `docker-compose.dev.yml` et par le
        // ConfigMap de déploiement ; là encore, il ne manquait que l'enregistrement.
        // (Cette ligne citait `infra/docker/env/catalog.env`, dossier retiré du dépôt
        // le 27 août.)
        // ═════════════════════════════════════════════════════════════════════════
        services.AddMediaGrpcClient(configuration);

        return services;
    }
}
