namespace HBA.FoodOrders.Domain.Orders;

public interface IMealOrderRepository
{
    Task AddAsync(MealOrder order, CancellationToken cancellationToken = default);

    Task<MealOrder?> GetByIdAsync(MealOrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealOrder>> ListByBuyerAsync(
        Guid buyerId, int take = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealOrder>> ListByRestaurantAsync(
        Guid restaurantId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il DÉJÀ acheté un repas pour de bon (Paid, Confirmed ou
    /// Delivered) ?
    /// </summary>
    Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>La commande née de CE panier, s'il y en a une.</summary>
    Task<MealOrder?> GetByCartAsync(Guid cartId, CancellationToken cancellationToken = default);
}
