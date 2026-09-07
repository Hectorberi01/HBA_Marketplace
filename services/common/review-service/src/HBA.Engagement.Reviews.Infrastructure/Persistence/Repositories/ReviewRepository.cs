using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using HBA.Engagement.Reviews.Domain.Reviews;

namespace HBA.Engagement.Reviews.Infrastructure.Persistence;

internal sealed class ReviewRepository : IReviewRepository
{
    private readonly ReviewsDbContext _dbContext;

    public ReviewRepository(ReviewsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Review review, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AddAsync(review, cancellationToken);

    public async Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Review>> ListByProductAsync(
        Guid productId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Published)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Review>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Review> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForModerationAsync(int page, int pageSize, ReviewStatus? status, CancellationToken cancellationToken = default)
    {
        var nu = _dbContext.Reviews.AsNoTracking();

        var comptes = await nu
            .GroupBy(r => r.Status)
            .Select(g => new { Statut = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        var filtre = status is { } etat ? nu.Where(r => r.Status == etat) : nu;

        var total = await filtre.CountAsync(cancellationToken);

        // Les plus anciens d'abord : une file de modération se traite par le bas.
        var elements = await filtre
            .OrderBy(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (elements, total, comptes.ToDictionary(x => x.Statut.ToString(), x => x.Nombre));
    }

    public async Task<bool> ExistsAsync(Guid buyerId, Guid productId, Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AnyAsync(
            r => r.BuyerId == buyerId && r.ProductId == productId && r.OrderId == orderId, cancellationToken);

    public async Task<ProductRating> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var (moyenne, total) = await AgregerAsync(
            r => r.ProductId == productId, cancellationToken);

        return new ProductRating(productId, moyenne, total);
    }

    public async Task<SellerRating> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var (moyenne, total) = await AgregerAsync(
            r => r.SellerId == sellerId, cancellationToken);

        return new SellerRating(sellerId, moyenne, total);
    }

    /// <summary>
    /// La moyenne et le compte des avis publiés, calculés SUR CINQ LIGNES AU PLUS.
    /// </summary>
    private async Task<(double Moyenne, int Total)> AgregerAsync(
        Expression<Func<Review, bool>> perimetre, CancellationToken cancellationToken)
    {
        var repartition = await _dbContext.Reviews
            .AsNoTracking()
            .Where(perimetre)
            .Where(r => r.Status == ReviewStatus.Published)
            .GroupBy(r => r.Rating)
            .Select(g => new { Note = g.Key, Compte = g.Count() })
            .ToListAsync(cancellationToken);

        var total = repartition.Sum(x => x.Compte);

        if (total == 0)
        {
            return (0d, 0);
        }

        var somme = repartition.Sum(x => (long)x.Note.Value * x.Compte);

        return (Math.Round((double)somme / total, 2), total);
    }
}
