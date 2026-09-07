using HBA.Food.Contracts;

namespace HBA.Food.Application.Abstractions;

/// <summary>Lecture de la VITRINE : ce qu'un client anonyme a le droit de voir.</summary>
public interface IStorefrontReader
{
    /// <summary>
    /// La page de vitrine. Ne rend QUE des établissements actifs — le filtre n'est
    /// pas paramétrable.
    /// </summary>
    Task<IReadOnlyList<RestaurantCardView>> ListAsync(
        int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// La fiche publique d'un établissement, ou <c> null</c> s'il n'existe pas OU
    /// s'il n'est pas en vitrine.
    /// </summary>
    Task<RestaurantSummary?> GetPublicAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);
}
