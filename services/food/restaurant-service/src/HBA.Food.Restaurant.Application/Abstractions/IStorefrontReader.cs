using HBA.Food.Contracts;

namespace HBA.Food.Application.Abstractions;

/// <summary>
/// Lecture de la VITRINE : ce qu'un client anonyme a le droit de voir.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CECI N'EST PAS SUR <c>IFoodModuleApi</c>. J'AI FAIT L'ERREUR.
///
/// J'avais d'abord ajouté <c>ListStorefrontAsync</c> à <c>IFoodModuleApi</c> —
/// l'interface était là, elle rendait déjà des établissements, le geste
/// paraissait évident.
///
/// Il a cassé la compilation : <c>IFoodModuleApi</c> est le contrat
/// INTER-MODULES, et il possède une seconde implémentation, <c>FoodGrpcClient</c>,
/// dans <c>HBA.Food.Contracts.Grpc</c>. Toute méthode ajoutée à ce contrat oblige
/// donc à définir une RPC dans le proto — pour une vitrine dont aucun autre module
/// n'a le moindre usage.
///
/// La leçon dépasse l'erreur : <c>IFoodModuleApi</c> répond à la question « que
/// les AUTRES modules ont-ils le droit de demander à Food ? ». La vitrine répond
/// à « que la surface HTTP de Food a-t-elle besoin de lire ? ». Deux questions,
/// deux contrats — et le second n'a aucune raison de traverser le réseau.
///
/// Précédent dans le module : <c>IFoodUnitOfWork</c>, déclaré ici et implémenté
/// dans Infrastructure.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IStorefrontReader
{
    /// <summary>
    /// La page de vitrine. Ne rend QUE des établissements actifs — le filtre
    /// n'est pas paramétrable.
    /// </summary>
    Task<IReadOnlyList<RestaurantCardView>> ListAsync(
        int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// La fiche publique d'un établissement, ou <c>null</c> s'il n'existe pas
    /// OU s'il n'est pas en vitrine.
    /// </summary>
    /// <remarks>
    /// LES DEUX CAS RENDENT `null`, ET C'EST VOULU.
    ///
    /// Les distinguer permettrait d'énumérer les établissements suspendus en
    /// comparant deux réponses. L'appelant traduit en 404 sans avoir à choisir.
    /// </remarks>
    Task<RestaurantSummary?> GetPublicAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);
}
