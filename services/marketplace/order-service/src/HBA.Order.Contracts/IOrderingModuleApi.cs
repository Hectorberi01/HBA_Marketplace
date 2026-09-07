namespace HBA.Orders.Contracts;

/// <summary>API in-process publique du module Ordering.</summary>
public interface IOrderingModuleApi
{
    Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<OrderReturnContext?> GetOrderReturnContextAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Cet acheteur a-t-il DÉJÀ passé une commande (hors commandes échouées) ?</summary>
    Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nombre d'articles VENDUS par un vendeur : somme des quantités de ses lignes
    /// dans les commandes réellement encaissées (Confirmed / Delivered).
    /// </summary>
    Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
