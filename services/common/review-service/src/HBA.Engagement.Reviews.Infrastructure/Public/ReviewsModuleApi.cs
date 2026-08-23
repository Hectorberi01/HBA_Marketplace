using MediatR;
using HBA.Engagement.Reviews.Application.Reviews.Queries;
using HBA.Engagement.Reviews.Contracts;

namespace HBA.Engagement.Reviews.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Reviews.</summary>
internal sealed class ReviewsModuleApi : IReviewsModuleApi
{
    private readonly ISender _sender;

    public ReviewsModuleApi(ISender sender) => _sender = sender;

    public async Task<ProductRatingSummary> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetProductRatingQuery(productId), cancellationToken);
        return result.IsSuccess ? result.Value : new ProductRatingSummary(productId, 0d, 0);
    }

    public async Task<SellerRatingSummary> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetSellerRatingQuery(sellerId), cancellationToken);
        return result.IsSuccess ? result.Value : new SellerRatingSummary(sellerId, 0d, 0);
    }
}
