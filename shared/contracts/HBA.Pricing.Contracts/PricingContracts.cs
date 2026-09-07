namespace HBA.Pricing.Contracts;

/// <summary>Demande de calcul de prix effectif pour une ligne (offre/variante).</summary>
/// <param name="BuyerId">L'acheteur. <c>default</c> = inconnu.</param>
/// <param name="CartSubtotal">Sous-total du PANIER entier, toutes lignes confondues.</param>
public sealed record PriceRequest(
    decimal BaseAmount,
    string Currency,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    int Quantity,
    decimal Subtotal,
    string? Code = null,
    bool IsFirstOrder = false,
    Guid BuyerId = default,
    decimal CartSubtotal = 0m);

/// <summary>
/// Résultat du calcul : prix de base, réductions tracées par financeur, prix final.
/// </summary>
public sealed record PriceBreakdownDto(
    decimal BaseAmount,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalAmount,
    string Currency);

/// <summary>Vue publique d'une promotion.</summary>
/// <param name="PerUserLimit">
/// <summary>Plafond par acheteur. 0 = illimité.</summary>
/// </param>
public sealed record PromotionSummary(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string FundedBy,
    string Type,
    decimal Value,
    string ScopeType,
    IReadOnlyList<Guid> Targets,
    string? Code,
    DateTime StartUtc,
    DateTime EndUtc,
    int UsageLimit,
    int UsedCount,
    string Status,
    int PerUserLimit = 0);

/// <summary>Verdict de validation d'un code promo, AVANT de l'attacher au panier.</summary>
public sealed record CouponValidation(bool IsValid, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static CouponValidation Valid() => new(true);

    public static CouponValidation Invalid(string code, string message) => new(false, code, message);
}
