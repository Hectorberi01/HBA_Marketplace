namespace HBA.Engagement.Recommendations.Domain.Recommendations;

public interface IRecommendationRepository
{
    Task AddAsync(Recommendation recommendation, CancellationToken cancellationToken = default);

    /// <summary>Recommandation existante pour une clé (contexte produit) — pour upsert.</summary>
    Task<Recommendation?> GetByProductAsync(RecommendationType type, Guid contextProductId, CancellationToken cancellationToken = default);

    /// <summary>Recommandation personnalisée existante d'un utilisateur — pour upsert.</summary>
    Task<Recommendation?> GetByUserAsync(RecommendationType type, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Une page de recommandations, tous contextes confondus (administration).</summary>
    Task<(IReadOnlyList<Recommendation> Items, int Total, IReadOnlyDictionary<string, int> TypeCounts)>
        ListAsync(int page, int pageSize, RecommendationType? type, CancellationToken cancellationToken = default);
}
