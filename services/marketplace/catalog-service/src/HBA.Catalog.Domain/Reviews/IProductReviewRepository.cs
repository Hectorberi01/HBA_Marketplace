using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Domain.Reviews;

/// <summary>Port de persistance des décisions d'administration.</summary>
public interface IProductReviewRepository
{
    Task AddAsync(ProductReview review, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les décisions rendues sur un produit, de la plus récente à la plus ancienne.
    /// </summary>
    Task<IReadOnlyList<ProductReview>> ListByProductAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>La dernière décision rendue sur un produit, s'il y en a une.</summary>
    Task<ProductReview?> GetLatestForProductAsync(
        Guid productId, CancellationToken cancellationToken = default);
}
