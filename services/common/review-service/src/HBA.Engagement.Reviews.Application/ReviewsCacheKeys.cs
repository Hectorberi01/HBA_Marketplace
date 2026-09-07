namespace HBA.Engagement.Reviews.Application;

/// <summary>Clés de cache du module Reviews.</summary>
public static class ReviewsCacheKeys
{
    /// <summary>Note agrégée d'un produit (moyenne + nombre d'avis).</summary>
    public static string Rating(Guid productId) => $"reviews:rating:{productId}";

    /// <summary>Avis publiés d'un produit (onglet « Avis » de la fiche).</summary>
    public static string ByProduct(Guid productId) => $"reviews:by-product:{productId}";

    /// <summary>10 minutes.</summary>
    public static readonly TimeSpan RatingTtl = TimeSpan.FromMinutes(10);
}
