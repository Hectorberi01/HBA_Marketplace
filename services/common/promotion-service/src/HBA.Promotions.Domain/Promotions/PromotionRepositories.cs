namespace HBA.Promotions.Domain.Promotions;

/// <summary>Accès aux campagnes.</summary>
public interface IPromotionRepository
{
    /// <summary>
    /// Charge une campagne AVEC ses conditions.
    ///
    /// SANS LES RÈGLES, `EnsureApplicable` DIT « OUI » À TOUT.
    ///
    /// Une campagne chargée sans sa collection `Rules` présente une liste vide, et
    /// la boucle d'évaluation ne trouve rien à refuser. Le chargement paresseux
    /// est désactivé dans ce dépôt : c'est donc à l'implémentation de faire
    /// l'inclusion, et l'oublier n'échoue nulle part — la remise part simplement
    /// sur des paniers qui n'y avaient pas droit.
    /// </summary>
    Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Les campagnes d'un univers, les plus récentes d'abord.</summary>
    /// <param name="ownerSellerId">
    /// LE FILTRE D'APPARTENANCE, ET IL N'EST PAS DÉCORATIF.
    ///
    /// `GET /api/v1/merchant/promotions` est ouverte au vendeur depuis le lot D28.
    /// Filtrer la réponse APRÈS l'avoir construite aurait laissé la requête
    /// ramener les campagnes de tous les marchands, avec leurs budgets et leurs
    /// taux — et il aurait suffi d'un `Take` mal placé, ou d'une pagination
    /// ajoutée plus tard, pour que la fuite revienne. Le filtre est donc DANS la
    /// requête.
    ///
    /// <c>null</c> veut dire « aucun filtre » : c'est la vue administrateur.
    /// </param>
    Task<IReadOnlyList<Promotion>> ListAsync(
        PromotionScope? scope, int take, Guid? ownerSellerId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux coupons.</summary>
public interface ICouponRepository
{
    /// <summary>
    /// Retrouve un coupon par son code, avec ses réservations.
    ///
    /// Le code est normalisé en majuscules à la création : la recherche doit
    /// l'être aussi, sinon « welcome10 » ne trouve rien alors que le coupon existe.
    /// </summary>
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les coupons portant au moins une retenue vivante dont l'échéance est passée.
    /// </summary>
    /// <remarks>
    /// L'ENTRÉE DU BALAYEUR D'ISSUE-053, ET SA SEULE.
    ///
    /// L'index partiel `ix_coupon_usages_expiring` existe depuis la migration
    /// initiale — posé pour « le ménage des retenues expirées », un ménage que
    /// personne n'a jamais écrit. C'est cette requête qui lui donne enfin un
    /// appelant.
    ///
    /// LE COUPON EST CHARGÉ AVEC TOUTES SES RÉSERVATIONS, PAS SEULEMENT LES
    /// EXPIRÉES. `Coupon.CountUses` compte sur la collection complète : filtrer
    /// l'inclusion ferait croire au domaine que les usages engagés n'existent pas,
    /// et les plafonds cesseraient de s'appliquer sur les coupons balayés.
    /// </remarks>
    Task<IReadOnlyList<Coupon>> ListWithExpiredHoldsAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrouve le coupon portant une réservation donnée.
    ///
    /// `CommitCoupon` et `ReleaseCoupon` ne reçoivent que l'identifiant de la
    /// RÉSERVATION — l'appelant n'a aucune raison de connaître le coupon, et lui
    /// demander de le transporter ferait de son exactitude une condition de
    /// justesse comptable.
    /// </summary>
    Task<Coupon?> GetByReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les coupons portant une réservation engagée sur cette commande.
    ///
    /// C'est l'entrée du consommateur d'annulation : l'événement
    /// `marketplace.order.cancelled` ne connaît que l'identifiant de commande.
    /// </summary>
    Task<IReadOnlyList<Coupon>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default);
}
