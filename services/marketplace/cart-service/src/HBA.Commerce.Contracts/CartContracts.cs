namespace HBA.Commerce.Contracts;

/// <summary>
/// Une option retenue sur un plat. Ni prix ni libellé : voir `CartItemOption`.
/// </summary>
public sealed record CartLineOptionSummary(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// Une ligne de panier.
///
/// DEUX NATURES, ET <paramref name="Kind"/> DIT LAQUELLE LIRE.
///
/// « Goods » renseigne l'offre, le SKU et le lieu d'expédition ; « Food »
/// renseigne le restaurant, le plat et ses options. Les champs de l'autre nature
/// sont vides. Déduire la nature de la nullité d'un champ marche jusqu'au jour où
/// l'on en ajoute un — d'où le discriminant explicite.
/// </summary>
/// <param name="LineId">
/// Identifiant de la LIGNE. Seul moyen de désigner un plat, qui peut figurer
/// deux fois dans un panier avec des options différentes.
/// </param>
public sealed record CartLineSummary(
    Guid LineId,
    string Kind,
    Guid OfferId,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal UnitBaseAmount,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    decimal LineTotal,
    string Currency,

    // ── Restauration : vides pour une ligne de marchandise ──────────────────
    Guid RestaurantId = default,
    Guid MenuItemId = default,
    string? Notes = null,
    IReadOnlyList<CartLineOptionSummary>? Options = null);

/// <summary>Panier valorisé (snapshot lignes + totaux calculés via Pricing).</summary>
/// <param name="Kind">
/// La nature du panier : « Goods », « Food », ou null s'il est vide.
///
/// Le checkout en a besoin AVANT de lire les lignes : un panier de repas ne
/// réserve pas de stock et ne suit pas la même chaîne d'exécution.
/// </param>
/// <param name="PromotionCode">
/// Code promo appliqué au panier, ou null. Ordering le fige dans la commande au
/// checkout.
/// </param>
public sealed record CartSummary(
    Guid CartId,
    Guid BuyerId,
    string Currency,
    string Status,
    string? Kind,
    IReadOnlyList<CartLineSummary> Lines,
    decimal Subtotal,
    decimal TotalSellerDiscount,
    decimal TotalPlatformDiscount,
    decimal GrandTotal,
    string? PromotionCode = null);
