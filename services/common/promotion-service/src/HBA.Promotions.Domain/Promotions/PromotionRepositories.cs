namespace HBA.Promotions.Domain.Promotions;

/// <summary>Accès aux campagnes.</summary>
public interface IPromotionRepository
{
    /// <summary>Charge une campagne AVEC ses conditions.</summary>
    Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Les campagnes d'un univers, les plus récentes d'abord.</summary>
    /// <param name="ownerSellerId">LE FILTRE D'APPARTENANCE, ET IL N'EST PAS DÉCORATIF.</param>
    Task<IReadOnlyList<Promotion>> ListAsync(
        PromotionScope? scope, int take, Guid? ownerSellerId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux coupons.</summary>
public interface ICouponRepository
{
    /// <summary>Retrouve un coupon par son code, avec ses réservations.</summary>
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les coupons portant au moins une retenue vivante dont l'échéance est passée.
    /// </summary>
    Task<IReadOnlyList<Coupon>> ListWithExpiredHoldsAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>Retrouve le coupon portant une réservation donnée.</summary>
    Task<Coupon?> GetByReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>Les coupons portant une réservation engagée sur cette commande.</summary>
    Task<IReadOnlyList<Coupon>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default);
}
