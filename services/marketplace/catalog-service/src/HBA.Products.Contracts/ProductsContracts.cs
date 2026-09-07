namespace HBA.Products.Contracts;

/// <summary>Une fiche produit, telle que les autres modules ont le droit de la voir.</summary>
public sealed record ProductSummary(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Slug,
    string Status,
    bool IsVisible,
    string? MainImageUrl,
    IReadOnlyList<string> Tags);

/// <summary>Une déclinaison vendable.</summary>
public sealed record VariantSummary(
    Guid Id,
    Guid ProductId,
    string? Sku,
    bool IsActive,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>Une offre, telle que le panier et la vitrine la lisent.</summary>
/// <param name="EffectivePrice">
/// Ce que l'acheteur paie AUJOURD'HUI : le prix promotionnel s'il court, le prix
/// courant sinon.
/// </param>
/// <param name="PromotionEndsOnUtc">
/// Fin de la remise. Nulle si pas de remise, ou remise sans échéance.
/// </param>
/// <param name="Condition">État du bien : New, Used, Refurbished.</param>
public sealed record OfferSummary(
    Guid Id,
    Guid ProductId,
    Guid VariantId,
    Guid StoreId,
    Guid SellerId,
    string? Sku,
    decimal BuyerPrice,
    decimal? PromotionalPrice,
    decimal EffectivePrice,
    DateTime? PromotionEndsOnUtc,

    string Currency,
    string Status,
    bool IsPurchasable,
    string Condition,

    int HandlingTimeDays,
    Guid ShipFromLocationId);

/// <summary>API EN PROCESSUS DU MODULE PRODUCTS.</summary>
public interface IProductsModuleApi
{
    Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    // `GetProductsAsync` (LE LOT) A ÉTÉ RETIRÉ.

    Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default);

    /// <summary>Les offres achetables d'un produit — la Buy Box.</summary>
    Task<IReadOnlyList<OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Retrouve les offres qui vendent une référence d'inventaire donnée.</summary>
    Task<IReadOnlyList<OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default);
}
