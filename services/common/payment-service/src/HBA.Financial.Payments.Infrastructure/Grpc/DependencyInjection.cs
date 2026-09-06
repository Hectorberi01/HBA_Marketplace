using HBA.Deliveries.Contracts.Grpc;
using HBA.Food.Contracts.Grpc;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Financial.Payments.Infrastructure.Grpc.Configuration;
namespace HBA.Financial.Payments.Infrastructure.Grpc;

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
    public static IServiceCollection AjouterClientsGrpcFinancialPayments(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterLesDestinationsGrpc();

        services.AddOrderingGrpcClient(configuration);

        services.AddFoodGrpcClient(configuration);

        // Commandes de repas : lues par `IPayableOrderReader` pour ouvrir leur paiement
        // (lot 6.1). À ne pas confondre avec la ligne au-dessus, qui vise
        // restaurant-service.
        services.AddFoodOrdersGrpcClient(configuration);

        services.AddMerchantsGrpcClient(configuration);

        // CE CLIENT NE SERT QU'À UNE AUTORISATION.
        //
        // `GET /api/financial/wallets/drivers/{driverId}` et son relevé doivent
        // vérifier que l'appelant EST ce livreur. Le jeton porte un identifiant
        // d'utilisateur, la route un identifiant de livreur ; seule delivery-service
        // connaît la correspondance (`GetDriverAccountAsync`). Faute de ce client, les
        // deux routes avaient été rangées chez l'admin — et l'écran « Gains » du BFF
        // livreur rendait 403 à tous les livreurs.
        //
        // Aucun flux métier ne passe par là : financial-service ne crée ni ne pilote
        // de course.
        services.AddDeliveryGrpcClient(configuration);

        return services;
    }
}
