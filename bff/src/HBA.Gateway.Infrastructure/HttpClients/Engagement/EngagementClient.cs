using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Engagement;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Engagement;

/// <inheritdoc cref="IEngagementClient" />
public sealed class EngagementClient : ServiceHttpClient, IEngagementClient
{
    public EngagementClient(HttpClient http, ILogger<EngagementClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Engagement;

    // CES TROIS ROUTES SONT AUTHENTIFIÉES CÔTÉ SERVICE.
    //
    // `MapAuthenticatedGroup("/api/engagement/reviews")` et
    // `MapAuthenticatedGroup("/api/engagement/recommendations")`. Le jeton de
    // l'appelant est propagé par `OutboundHeaderPropagationHandler` ; sans
    // session, la réponse est un 401 que l'agrégateur traite comme une absence.
    public Task<ServiceResult<ProductRating>> GetProductRatingAsync(
        Guid productId, CancellationToken cancellationToken)
        => GetAsync<ProductRating>(
            $"/api/engagement/reviews/product/{productId}/rating", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<ProductReview>>> ListProductReviewsAsync(
        Guid productId, CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<ProductReview>>(
            $"/api/engagement/reviews/product/{productId}", cancellationToken);

    public Task<ServiceResult<RecommendationSet>> GetMyRecommendationsAsync(
        CancellationToken cancellationToken)
        => GetAsync<RecommendationSet>(
            "/api/engagement/recommendations/me", cancellationToken);
}
