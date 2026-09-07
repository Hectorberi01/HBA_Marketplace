namespace HBA.Engagement.Reviews.Contracts;

/// <summary>API in-process publique du module Reviews.</summary>
public interface IReviewsModuleApi
{
    Task<ProductRatingSummary> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Note agrégée d'un vendeur (moyenne + nombre d'avis publiés sur ses
    /// produits).
    /// </summary>
    Task<SellerRatingSummary> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
