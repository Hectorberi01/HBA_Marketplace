namespace HBA.Orders.Domain.Orders;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commandes d'un acheteur, de la plus récente à la plus ancienne, dans la
    /// limite de <paramref name="take"/> .
    /// </summary>
    Task<IReadOnlyList<Order>> ListByBuyerAsync(
        Guid buyerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il DÉJÀ acheté pour de bon ? (statut Paid, Confirmed ou
    /// Delivered)
    /// </summary>
    Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>La commande née de CE panier, s'il y en a une.</summary>
    Task<Order?> GetByCartAsync(Guid cartId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commandes comportant au moins une ligne vendue par ce vendeur, de la plus
    /// récente à la plus ancienne, dans la limite de <paramref name="take"/> .
    /// </summary>
    Task<IReadOnlyList<Order>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Somme des quantités vendues par ce vendeur sur les commandes encaissées.</summary>
    Task<int> SumSoldQuantityBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Toutes les commandes de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Order>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de commandes pour la console admin : filtre par statut, recherche par
    /// identifiant (commande ou acheteur, GUID exact), tri par date décroissante.
    /// </summary>
    Task<(IReadOnlyList<Order> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, OrderStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);
}
