namespace HBA.FoodCarts.Contracts;

/// <summary>
/// API publique du panier de restauration.
///
/// food-order-service l'appelle au moment de passer commande, pour récupérer le
/// panier valorisé et le figer — exactement comme order-service lit
/// `ICartModuleApi` du côté marketplace. Aucun accès direct à la base du panier.
/// </summary>
public interface IFoodCartModuleApi
{
    Task<FoodCartSummary?> GetActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<FoodCartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default);
}
