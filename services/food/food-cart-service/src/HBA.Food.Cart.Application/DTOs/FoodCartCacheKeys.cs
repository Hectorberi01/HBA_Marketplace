namespace HBA.FoodCarts.Application.Carts;

/// <summary>Clés de cache du panier de restauration (cache-aside via ICacheService).</summary>
internal static class FoodCartCacheKeys
{
    /// <summary>
    /// PRÉFIXE DISTINCT DE CELUI DE LA MARKETPLACE.
    ///
    /// L'ancien panier utilisait `cart:active:{buyerId}`. Les deux services
    /// partagent la même instance Redis : garder la même clé ferait qu'un ajout
    /// au panier de repas rendrait le panier de marchandise du même client — un
    /// `FoodCartSummary` désérialisé en `CartSummary`, avec des champs manquants
    /// et aucune erreur.
    /// </summary>
    public static string Active(Guid buyerId) => $"food_cart:active:{buyerId}";
}
