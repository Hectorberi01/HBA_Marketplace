using HBA.Gateway.Application.Contracts.Engagement;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>engagement-service</c> — avis, notes, recommandations, envies.</summary>
/// <remarks>
/// TOUTES SES ROUTES SONT AUTHENTIFIÉES CÔTÉ SERVICE.
///
/// Y compris la note d'un produit et les avis. Un appel émis pour un visiteur
/// anonyme reviendra en 401 : ce n'est pas une panne, et l'agrégateur doit le
/// traiter comme une absence normale plutôt que comme une dégradation.
/// </remarks>
public interface IEngagementClient : IServiceClient
{
    /// <summary><c>GET /api/engagement/reviews/product/{productId}/rating</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<ProductRating>> GetProductRatingAsync(
        Guid productId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/engagement/reviews/product/{productId}</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<IReadOnlyList<ProductReview>>> ListProductReviewsAsync(
        Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /api/engagement/recommendations/me</c> — AUTHENTIFIÉ.
    /// Rend un <see cref="RecommendationSet"/>, pas une liste d'identifiants.
    /// </summary>
    Task<ServiceResult<RecommendationSet>> GetMyRecommendationsAsync(
        CancellationToken cancellationToken);
}
