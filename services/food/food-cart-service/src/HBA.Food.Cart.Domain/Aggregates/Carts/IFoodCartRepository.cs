namespace HBA.FoodCarts.Domain.Carts;

public interface IFoodCartRepository
{
    Task AddAsync(FoodCart cart, CancellationToken cancellationToken = default);

    Task<FoodCart?> GetByIdAsync(FoodCartId id, CancellationToken cancellationToken = default);

    /// <summary>Panier de restauration actif de l'acheteur (un seul à la fois).</summary>
    Task<FoodCart?> GetActiveByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
