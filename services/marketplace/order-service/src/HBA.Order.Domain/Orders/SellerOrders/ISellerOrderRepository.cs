namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>Accès aux parts vendeur d'une commande.</summary>
public interface ISellerOrderRepository
{
    Task AddRangeAsync(IEnumerable<SellerOrder> sellerOrders, CancellationToken cancellationToken = default);

    /// <summary>La part de CE vendeur dans CETTE commande, ou <c>null</c>.</summary>
    Task<SellerOrder?> FindAsync(Guid orderId, Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Les parts d'une commande, tous vendeurs confondus.</summary>
    Task<IReadOnlyList<SellerOrder>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Le carnet d'un vendeur.</summary>
    /// <summary>
    /// Parts vendeur, de la plus récente à la plus ancienne, dans la limite de
    /// <paramref name="take"/> — qui DOIT être la même que celle passée à <c>
    /// IOrderRepository.ListBySellerAsync</c> quand les deux sont jointes.
    /// </summary>
    Task<IReadOnlyList<SellerOrder>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Cette commande a-t-elle DÉJÀ été découpée ?</summary>
    Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
