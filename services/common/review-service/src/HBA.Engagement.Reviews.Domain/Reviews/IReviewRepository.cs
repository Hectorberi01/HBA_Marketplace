namespace HBA.Engagement.Reviews.Domain.Reviews;

/// <summary>Note agrégée d'un produit (moyenne + nombre d'avis publiés).</summary>
public sealed record ProductRating(Guid ProductId, double Average, int Count);

/// <summary>Note agrégée d'un vendeur (moyenne + nombre d'avis publiés sur ses produits).</summary>
public sealed record SellerRating(Guid SellerId, double Average, int Count);

public interface IReviewRepository
{
    Task AddAsync(Review review, CancellationToken cancellationToken = default);

    Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Review>> ListByProductAsync(
        Guid productId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les avis ciblant les produits d'un vendeur (back-office vendeur).</summary>
    Task<IReadOnlyList<Review>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Une page d'avis pour la modération, filtrable par statut.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE MANQUAIT, ET SANS ELLE LA MODÉRATION N'EXISTAIT PAS.
    ///
    /// `flag`, `reject` et `restore` sont montés depuis le début sur le groupe
    /// d'administration — mais adressés par identifiant d'avis. Aucune route ne
    /// disait QUELS avis attendent. Un avis signalé restait donc `Flagged` jusqu'à
    /// ce que quelqu'un tombe dessus par la fiche produit : c'est-à-dire jamais,
    /// puisque `ListByProductAsync` ne rend que le publié.
    ///
    /// LE COMPTE PAR STATUT EST CALCULÉ AVANT LE FILTRE — trois statuts
    /// seulement (`Published`, `Flagged`, `Rejected`), et c'est le nombre de
    /// `Flagged` qui dit s'il y a du travail.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<(IReadOnlyList<Review> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForModerationAsync(int page, int pageSize, ReviewStatus? status, CancellationToken cancellationToken = default);

    /// <summary>Un acheteur ne peut déposer qu'un avis par produit et par commande.</summary>
    Task<bool> ExistsAsync(Guid buyerId, Guid productId, Guid orderId, CancellationToken cancellationToken = default);

    Task<ProductRating> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Note agrégée sur TOUS les produits d'un vendeur (avis publiés).</summary>
    Task<SellerRating> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
