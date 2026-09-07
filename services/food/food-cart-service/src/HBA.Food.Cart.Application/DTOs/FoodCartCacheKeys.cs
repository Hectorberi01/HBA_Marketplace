namespace HBA.FoodCarts.Application.Carts;

/// <summary>Clés de cache du panier de restauration (cache-aside via ICacheService).</summary>
internal static class FoodCartCacheKeys
{
    /// <summary>PRÉFIXE DISTINCT DE CELUI DE LA MARKETPLACE.</summary>
    public static string Active(Guid buyerId) => $"food_cart:active:{buyerId}";
}
