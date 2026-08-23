using HBA.Gateway.Application.Contracts.Order;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>order-service</c>.</summary>
public interface IOrderClient : IServiceClient
{
    /// <summary>
    /// <c>GET /api/orders/</c> — les commandes de l'appelant. AUTHENTIFIÉ.
    /// </summary>
    /// <remarks>
    /// AUCUN FILTRE DE STATUT N'EST EXPOSÉ PAR LE SERVICE.
    ///
    /// La route rend TOUTES les commandes de l'acheteur, sans pagination ni
    /// paramètre. Isoler « la commande en cours » se fait donc côté passerelle,
    /// sur une liste dont la taille croît avec l'ancienneté du compte.
    ///
    /// Manque à combler : <c>GET /api/orders?status=...&amp;page=...</c>. Tant
    /// qu'il n'existe pas, cette méthode est correcte mais coûteuse, et le coût
    /// est porté par les clients les plus fidèles — ceux qu'on veut le moins
    /// faire attendre.
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListMineAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /api/sellers/{sellerId}/orders</c> — AUTHENTIFIÉ.
    /// </summary>
    /// <remarks>
    /// NI PAGINATION, NI FILTRE, NI PÉRIODE.
    ///
    /// La route rend TOUTES les commandes du vendeur depuis l'ouverture du
    /// compte. Le tableau de bord n'en affiche que les dernières et n'en compte
    /// que celles du jour : le tri, le filtrage et la troncature se font donc
    /// après réception, sur une liste qui grandit avec le succès de la boutique.
    ///
    /// Manque à combler : <c>?from=&amp;to=&amp;status=&amp;page=</c>.
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken);
}
