using Microsoft.EntityFrameworkCore;
using HBA.Catalog.Domain.Reviews;

namespace HBA.Catalog.Infrastructure.Persistence;

internal sealed class ProductReviewRepository : IProductReviewRepository
{
    private readonly CatalogDbContext _dbContext;

    public ProductReviewRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(ProductReview review, CancellationToken cancellationToken = default)
        => await _dbContext.ProductReviews.AddAsync(review, cancellationToken);

    public async Task<IReadOnlyList<ProductReview>> ListByProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
        => await _dbContext.ProductReviews
            .AsNoTracking()
            .Include(r => r.Reasons)
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.ReviewedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<ProductReview?> GetLatestForProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
        => await _dbContext.ProductReviews
            .AsNoTracking()
            .Include(r => r.Reasons)
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.ReviewedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
