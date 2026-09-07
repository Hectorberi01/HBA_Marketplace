namespace HBA.Catalog.Contracts;

/// <summary>API in-process publique du module Catalog.</summary>
public interface ICatalogModuleApi
{
    Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Produits « mis en avant » : actifs et porteurs du tag <c>featured</c>.</summary>
    Task<IReadOnlyList<ProductSummary>> ListFeaturedAsync(int max, CancellationToken cancellationToken = default);

    Task<BrandSummary?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken = default);

    Task<CategorySummary?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
