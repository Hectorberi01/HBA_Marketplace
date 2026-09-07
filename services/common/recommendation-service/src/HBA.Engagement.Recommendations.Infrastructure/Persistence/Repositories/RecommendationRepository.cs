using Microsoft.EntityFrameworkCore;
using HBA.Engagement.Recommendations.Domain.Recommendations;

namespace HBA.Engagement.Recommendations.Infrastructure.Persistence;

internal sealed class RecommendationRepository : IRecommendationRepository
{
    private readonly RecommendationsDbContext _dbContext;

    public RecommendationRepository(RecommendationsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Recommendation recommendation, CancellationToken cancellationToken = default)
        => await _dbContext.Recommendations.AddAsync(recommendation, cancellationToken);

    public async Task<Recommendation?> GetByProductAsync(RecommendationType type, Guid contextProductId, CancellationToken cancellationToken = default)
        => await _dbContext.Recommendations
            .FirstOrDefaultAsync(r => r.Type == type && r.ContextProductId == contextProductId, cancellationToken);

    public async Task<Recommendation?> GetByUserAsync(RecommendationType type, Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Recommendations
            .FirstOrDefaultAsync(r => r.Type == type && r.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<Recommendation> Items, int Total, IReadOnlyDictionary<string, int> TypeCounts)>
        ListAsync(int page, int pageSize, RecommendationType? type, CancellationToken cancellationToken = default)
    {
        var nu = _dbContext.Recommendations.AsNoTracking();

        var comptes = await nu
            .GroupBy(r => r.Type)
            .Select(g => new { Type = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        var filtre = type is { } demande ? nu.Where(r => r.Type == demande) : nu;

        var total = await filtre.CountAsync(cancellationToken);

        // Les plus récentes d'abord : une recommandation est un calcul daté, et
        // celle qui compte est la dernière posée.
        var elements = await filtre
            .OrderByDescending(r => r.GeneratedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (elements, total, comptes.ToDictionary(x => x.Type.ToString(), x => x.Nombre));
    }
}
