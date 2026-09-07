namespace HBA.Gateway.Application.Contracts.Engagement;

/// <summary>Note agrégée d'un produit.</summary>
public sealed record ProductRating(Guid ProductId, double Average, int Count);

public sealed record ProductReview(
    Guid Id,
    Guid ProductId,
    int Rating,
    string Title,
    string Body,
    bool IsVerifiedPurchase,
    DateTime CreatedAtUtc,
    string? SellerReply);

/// <summary>Un jeu de recommandations.</summary>
public sealed record RecommendationSet(
    Guid Id,
    string Type,
    Guid? ContextProductId,
    Guid? UserId,
    IReadOnlyList<Guid> RecommendedProductIds,
    double Score,
    DateTime GeneratedAtUtc);
