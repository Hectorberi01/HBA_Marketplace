using HBA.Gateway.Application.Contracts.Catalog;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>catalog-service</c> — produits, catégories, marques.</summary>
public interface ICatalogClient : IServiceClient
{
    /// <summary><c>GET /api/catalog/products/{id}</c> — route publique, anonyme.</summary>
    Task<ServiceResult<CatalogProduct>> GetProductAsync(Guid id, CancellationToken cancellationToken);

    /// <summary><c>GET /api/catalog/categories</c> — route publique, anonyme.</summary>
    Task<ServiceResult<IReadOnlyList<CatalogCategory>>> ListCategoriesAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/catalog/sellers/{sellerId}/products</c> — route publique.</summary>
    Task<ServiceResult<IReadOnlyList<CatalogProduct>>> ListSellerProductsAsync(
        Guid sellerId, CancellationToken cancellationToken);
}
