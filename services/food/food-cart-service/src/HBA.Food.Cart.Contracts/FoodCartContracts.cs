namespace HBA.FoodCarts.Contracts;

/// <summary>Une option retenue sur un plat.</summary>
public sealed record FoodCartLineOptionSummary(Guid OptionGroupId, Guid OptionId);

/// <summary>Une ligne de panier de restauration.</summary>
/// <param name="LineId">
/// Identifiant de la LIGNE, et seul moyen de la désigner : le même plat peut
/// figurer deux fois avec des options différentes.
/// </param>
public sealed record FoodCartLineSummary(
    Guid LineId,
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitBaseAmount,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    decimal LineTotal,
    string Currency,
    string? Notes,
    IReadOnlyList<FoodCartLineOptionSummary> Options);

/// <summary>Panier de restauration valorisé : lignes figées, totaux calculés via Pricing.</summary>
/// <param name="RestaurantId">L'établissement du panier.</param>
public sealed record FoodCartSummary(
    Guid CartId,
    Guid BuyerId,
    Guid? RestaurantId,
    string Currency,
    string Status,
    IReadOnlyList<FoodCartLineSummary> Lines,
    decimal Subtotal,
    decimal TotalSellerDiscount,
    decimal TotalPlatformDiscount,
    decimal GrandTotal,
    string? PromotionCode = null);
