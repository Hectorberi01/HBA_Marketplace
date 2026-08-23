namespace HBA.FoodCarts.Contracts;

/// <summary>Une option retenue sur un plat. Ni prix ni libellé : voir <c>FoodCartItemOption</c>.</summary>
public sealed record FoodCartLineOptionSummary(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// Une ligne de panier de restauration.
///
/// AUCUN DISCRIMINANT, ET AUCUN CHAMP VIDE.
///
/// Son ancêtre `CartLineSummary` portait quinze champs dont huit n'avaient de
/// sens que pour la marchandise, plus un `Kind` pour dire lesquels lire. Tout
/// consommateur devait connaître la règle « si Kind vaut Food, ignorer OfferId,
/// ProductId, CategoryId, SellerId, Sku, ShipFromLocationId » — une règle qui ne
/// se vérifie pas à la compilation et qu'on oublie.
/// </summary>
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

/// <summary>
/// Panier de restauration valorisé : lignes figées, totaux calculés via Pricing.
/// </summary>
/// <param name="RestaurantId">
/// L'établissement du panier. <c>null</c> quand le panier n'existe pas encore.
///
/// SUR LE PANIER, PAS DÉDUIT DE LA PREMIÈRE LIGNE. C'est une propriété du
/// panier depuis la séparation, et un panier vide en a une : c'est l'ouverture
/// qui la fixe.
/// </param>
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
