namespace HBA.Gateway.Application.Contracts.Merchant;

/// <summary>Vitrine d'une boutique.</summary>
public sealed record StoreShowcase(
    Guid Id,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    bool IsSelling);

/// <summary>
/// Le dossier vendeur du compte connecté — miroir PARTIEL de <c> SellerSummary</c>.
/// </summary>
public sealed record SellerAccount(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string Status,
    string KybStatus,
    decimal CommissionRate,
    decimal Rating,
    int SalesCount);

/// <summary>Une boutique, vue par son vendeur — miroir de <c>StoreSummary</c>.</summary>
public sealed record MerchantStore(
    Guid Id,
    Guid SellerId,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    string Status,
    bool IsSelling,
    string? StatusReason);
