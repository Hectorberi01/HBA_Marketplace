using HBA.Gateway.Application.Contracts.Engagement;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>
/// Client sortant vers <c> engagement-service</c> — avis, notes, recommandations,
/// envies.
/// </summary>
public interface IEngagementClient : IServiceClient
{
    /// <summary>
    /// <c> GET /api/engagement/reviews/product/{productId}/rating</c> —
    /// AUTHENTIFIÉ.
    /// </summary>
    Task<ServiceResult<ProductRating>> GetProductRatingAsync(
        Guid productId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/engagement/reviews/product/{productId}</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<IReadOnlyList<ProductReview>>> ListProductReviewsAsync(
        Guid productId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/engagement/recommendations/me</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<RecommendationSet>> GetMyRecommendationsAsync(
        CancellationToken cancellationToken);
}
