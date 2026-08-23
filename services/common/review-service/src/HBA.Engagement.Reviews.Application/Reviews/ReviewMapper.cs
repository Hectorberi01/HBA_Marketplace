using HBA.Engagement.Reviews.Contracts;
using HBA.Engagement.Reviews.Domain.Reviews;

namespace HBA.Engagement.Reviews.Application.Reviews;

internal static class ReviewMapper
{
    public static ReviewSummary ToSummary(Review r) => new(
        r.Id.Value,
        r.ProductId,
        r.SellerId,
        r.BuyerId,
        r.Rating.Value,
        r.Title,
        r.Body,
        r.IsVerifiedPurchase,
        r.Status.ToString(),
        r.CreatedAtUtc,
        r.SellerReply,
        r.SellerRepliedAtUtc);
}
