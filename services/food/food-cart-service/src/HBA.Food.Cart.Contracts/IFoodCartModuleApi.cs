namespace HBA.FoodCarts.Contracts;

/// <summary>API publique du panier de restauration.</summary>
public interface IFoodCartModuleApi
{
    Task<FoodCartSummary?> GetActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<FoodCartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default);
}
