using HBA.Promotions.Domain.Promotions.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Domain.Promotions;

/// <summary>État d'une réservation de coupon.</summary>
public enum CouponReservationStatus
{
    /// <summary>Réservée au checkout, pas encore engagée par un paiement.</summary>
    Held = 0,

    /// <summary>Engagée : la commande est payée, l'usage est définitif.</summary>
    Committed = 1,

    /// <summary>Libérée — expiration, abandon, ou annulation de commande.</summary>
    Released = 2
}

/// <summary>
/// Ce qu'un balayage d'expiration a libéré sur UN coupon.
/// </summary>
/// <param name="Count">Retenues passées en <c>Released</c>.</param>
/// <param name="Amount">Budget à rendre à la campagne — le chiffre que l'audit réclame.</param>
public sealed record CouponHoldExpiry(int Count, long Amount)
{
    public static readonly CouponHoldExpiry Empty = new(0, 0);

    public bool IsEmpty => Count == 0;
}

/// <summary>
/// Code d'accès à une promotion (§10.16, table <c>coupons</c>).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PLAFOND PAR UTILISATEUR SE COMPTE SUR LES USAGES ENGAGÉS **ET** RETENUS.
///
/// Ne compter que les usages engagés laisserait un même compte ouvrir cent paniers
/// et retenir cent fois le coupon avant d'en payer un seul : le budget global
/// s'épuise sans qu'aucune limite individuelle ne soit dépassée. Ne compter que
/// les retenues serait l'inverse — un client qui abandonne un panier se
/// verrouillerait lui-même.
///
/// On compte donc les deux, et l'expiration des retenues répare le second cas :
/// une retenue abandonnée cesse de compter au bout de son délai, sans intervention.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Coupon : AggregateRoot<Guid>
{
    /// <summary>Durée d'une retenue. Assez pour finir un paiement, trop court pour bloquer une campagne.</summary>
    public static readonly TimeSpan HoldLifetime = TimeSpan.FromMinutes(30);

    private readonly List<CouponReservation> _reservations = new();

    private Coupon(Guid id, Guid promotionId, string code, int? maxUses, int? perUserLimit)
        : base(id)
    {
        PromotionId = promotionId;
        Code = code;
        MaxUses = maxUses;
        PerUserLimit = perUserLimit;
        CreatedAtUtc = DateTime.UtcNow;
    }

    private Coupon() => Code = string.Empty;

    public Guid PromotionId { get; private set; }

    /// <summary>Code saisi par le client, normalisé en majuscules.</summary>
    public string Code { get; private set; }

    /// <summary>Plafond global d'usages. Null = illimité (le budget de la campagne reste la borne).</summary>
    public int? MaxUses { get; private set; }

    /// <summary>Plafond par compte. Null = illimité.</summary>
    public int? PerUserLimit { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<CouponReservation> Reservations => _reservations.AsReadOnly();

    public static Result<Coupon> Create(Guid promotionId, string? code, int? maxUses, int? perUserLimit)
    {
        if (promotionId == Guid.Empty)
        {
            return Result.Failure<Coupon>(Error.Validation(
                "promotions.coupon.promotion_required", "Un coupon doit être rattaché à une campagne."));
        }

        var normalise = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (normalise.Length < 3)
        {
            // Trois caractères ne sont pas une politique de sécurité, c'est un
            // garde-fou de saisie. Un code court se devine ; la vraie protection
            // reste le plafond d'usages et le budget.
            return Result.Failure<Coupon>(Error.Validation(
                "promotions.coupon.code_too_short", "Le code doit faire au moins trois caractères."));
        }

        if (maxUses is <= 0 || perUserLimit is <= 0)
        {
            return Result.Failure<Coupon>(Error.Validation(
                "promotions.coupon.limit_invalid", "Un plafond défini doit être positif."));
        }

        return new Coupon(Guid.NewGuid(), promotionId, normalise, maxUses, perUserLimit);
    }

    /// <summary>Usages qui comptent : engagés, plus retenues encore vivantes.</summary>
    public int CountUses(DateTime nowUtc)
        => _reservations.Count(r => r.CountsAt(nowUtc));

    public int CountUsesBy(Guid userId, DateTime nowUtc)
        => _reservations.Count(r => r.UserId == userId && r.CountsAt(nowUtc));

    /// <summary>
    /// Retient le coupon pour un panier. C'est la première phase des deux du §10.16
    /// (`ReserveCoupon` puis `CommitCoupon`).
    /// </summary>
    public Result<CouponReservation> Reserve(Guid userId, Guid cartId, long discountAmount, DateTime nowUtc)
    {
        if (discountAmount <= 0)
        {
            return Result.Failure<CouponReservation>(Error.Validation(
                "promotions.coupon.amount_invalid", "Le montant de la remise doit être positif."));
        }

        // Une même panier ne retient qu'une fois : un double clic sur « appliquer »
        // ne doit pas consommer deux usages ni deux fois le budget.
        var existante = _reservations.FirstOrDefault(
            r => r.CartId == cartId && r.Status == CouponReservationStatus.Held && !r.HasExpired(nowUtc));

        if (existante is not null)
        {
            return Result.Success(existante);
        }

        if (MaxUses is not null && CountUses(nowUtc) >= MaxUses.Value)
        {
            return Result.Failure<CouponReservation>(Error.BusinessRule(
                "promotions.coupon.max_uses_reached", "Ce coupon a atteint son nombre maximal d'utilisations."));
        }

        if (PerUserLimit is not null && CountUsesBy(userId, nowUtc) >= PerUserLimit.Value)
        {
            return Result.Failure<CouponReservation>(Error.BusinessRule(
                "promotions.coupon.per_user_limit_reached", "Vous avez déjà utilisé ce coupon."));
        }

        var reservation = new CouponReservation(
            Guid.NewGuid(), Id, userId, cartId, discountAmount, nowUtc.Add(HoldLifetime), nowUtc);

        _reservations.Add(reservation);
        return Result.Success(reservation);
    }

    /// <summary>
    /// Engage une retenue : la commande est payée, l'usage devient définitif.
    ///
    /// L'ÉVÉNEMENT `coupon.used` NE PART QU'AU PREMIER ENGAGEMENT.
    ///
    /// Kafka livre au moins une fois, donc le rejeu est la norme, pas l'exception.
    /// `CouponReservation.Commit` rend déjà `Success` sur un rejeu — c'est ce qui
    /// rend l'opération sûre. Mais publier depuis cette branche compterait un
    /// second usage côté marketing pour une seule commande, et la remise annoncée
    /// ne correspondrait plus à aucune ligne comptable. D'où l'état lu AVANT.
    /// </summary>
    public Result Commit(Guid reservationId, Guid orderId, DateTime nowUtc)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);

        if (reservation is null)
        {
            return Result.Failure(Error.NotFound(
                "promotions.coupon.reservation_not_found", "Cette réservation est introuvable."));
        }

        var dejaEngagee = reservation.Status == CouponReservationStatus.Committed;

        var resultat = reservation.Commit(orderId, nowUtc);

        if (resultat.IsSuccess && !dejaEngagee)
        {
            Raise(new CouponUsedDomainEvent(
                Id, PromotionId, Code, reservation.UserId, orderId, reservation.DiscountAmount));
        }

        return resultat;
    }

    /// <summary>
    /// Libère toutes les retenues de ce coupon dont l'échéance est passée, et dit
    /// combien de budget rendre à la campagne.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// AUCUN BALAYEUR NE LISAIT `ExpiresAtUtc` (ISSUE-053).
    ///
    /// L'échéance était calculée à chaque retenue, écrite en base depuis la
    /// migration initiale… et relue par PERSONNE. `HoldLifetime` valait donc trente
    /// minutes sur le papier et l'infini en pratique : chaque panier abandonné
    /// retirait sa remise du budget de la campagne, DÉFINITIVEMENT.
    ///
    /// L'encadré d'`IPromotionModuleApi` affirme que « la compensation ne dépend pas
    /// de la bonne volonté — ni de la survie — de celui qui l'a demandée ». Cette
    /// méthode est ce qui rend la phrase vraie ; sans elle, la compensation dépendait
    /// exactement de cela, et une campagne passait `Exhausted` sur des paniers que
    /// personne n'avait jamais payés.
    ///
    /// IDEMPOTENTE PAR CONSTRUCTION.
    ///
    /// `Release` fait passer la retenue en `Released` ; `HasExpired` ne rend vrai que
    /// pour une retenue `Held`. Un second passage ne trouve donc plus rien et rend 0.
    /// Rejouer le balayage, ou l'interrompre au milieu, ne recrédite jamais deux fois.
    ///
    /// UN USAGE `Committed` N'EST JAMAIS TOUCHÉ, MÊME LARGEMENT EXPIRÉ.
    ///
    /// C'est une vente payée : son budget est dû. `HasExpired` l'exclut déjà — la
    /// garde est dans l'entité, pas dans l'appelant, pour qu'un second appelant ne
    /// puisse pas l'oublier.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <returns>Nombre de retenues libérées et montant total à rendre au budget.</returns>
    public CouponHoldExpiry ExpireHolds(DateTime nowUtc)
    {
        var expirees = _reservations.Where(r => r.HasExpired(nowUtc)).ToList();

        if (expirees.Count == 0)
        {
            return CouponHoldExpiry.Empty;
        }

        var aRendre = 0L;

        foreach (var reservation in expirees)
        {
            // Le montant est lu AVANT la libération : après, le statut vaut
            // `Released` et plus rien ne dit qu'un budget avait été engagé.
            aRendre += reservation.DiscountAmount;
            reservation.Release();
        }

        return new CouponHoldExpiry(expirees.Count, aRendre);
    }

    /// <summary>Libère une retenue — abandon, expiration, ou annulation de commande.</summary>
    public Result Release(Guid reservationId)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);

        if (reservation is null)
        {
            return Result.Failure(Error.NotFound(
                "promotions.coupon.reservation_not_found", "Cette réservation est introuvable."));
        }

        reservation.Release();
        return Result.Success();
    }

    /// <summary>
    /// La commande a été annulée : on rend au client son droit d'usage, et l'on
    /// dit à l'appelant combien de budget rendre à la campagne.
    ///
    /// CETTE MÉTHODE EST NATURELLEMENT IDEMPOTENTE, ET ELLE DOIT L'ÊTRE.
    ///
    /// `marketplace.order.cancelled` sera livré au moins une fois. Un second
    /// passage ne trouve plus d'usage engagé sur cette commande et rend 0 : le
    /// budget n'est crédité qu'une fois. Compter sur un garde d'inbox aurait
    /// suffi la plupart du temps — mais « la plupart du temps » sur un budget se
    /// solde par une campagne qui semble ne jamais s'épuiser.
    /// </summary>
    /// <returns>Le montant total à rendre au budget de la campagne.</returns>
    public long RevokeForCancelledOrder(Guid orderId)
    {
        var engagees = _reservations
            .Where(r => r.OrderId == orderId && r.Status == CouponReservationStatus.Committed)
            .ToList();

        var aRendre = 0L;

        foreach (var reservation in engagees)
        {
            reservation.Revoke();
            aRendre += reservation.DiscountAmount;
        }

        return aRendre;
    }
}

/// <summary>
/// Retenue puis usage d'un coupon (§10.16, table <c>coupon_usages</c>).
/// </summary>
public sealed class CouponReservation : Entity<Guid>
{
    internal CouponReservation(
        Guid id, Guid couponId, Guid userId, Guid cartId,
        long discountAmount, DateTime expiresAtUtc, DateTime createdAtUtc)
        : base(id)
    {
        CouponId = couponId;
        UserId = userId;
        CartId = cartId;
        DiscountAmount = discountAmount;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
        Status = CouponReservationStatus.Held;
    }

    private CouponReservation()
    {
    }

    public Guid CouponId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CartId { get; private set; }

    /// <summary>Commande qui a engagé la retenue. Null tant qu'elle est retenue ou libérée.</summary>
    public Guid? OrderId { get; private set; }

    public long DiscountAmount { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? CommittedAtUtc { get; private set; }

    public CouponReservationStatus Status { get; private set; }

    public bool HasExpired(DateTime nowUtc)
        => Status == CouponReservationStatus.Held && nowUtc > ExpiresAtUtc;

    /// <summary>
    /// Cette retenue compte-t-elle dans les plafonds à cet instant ?
    ///
    /// Un usage engagé compte toujours. Une retenue ne compte que tant qu'elle est
    /// vivante — sinon un panier abandonné bloquerait son auteur indéfiniment.
    /// </summary>
    public bool CountsAt(DateTime nowUtc)
        => Status == CouponReservationStatus.Committed
           || (Status == CouponReservationStatus.Held && !HasExpired(nowUtc));

    internal Result Commit(Guid orderId, DateTime nowUtc)
    {
        if (Status == CouponReservationStatus.Committed)
        {
            // Rejeu : Kafka livre au moins une fois, et engager deux fois la même
            // retenue compterait deux usages pour une commande.
            return Result.Success();
        }

        if (Status == CouponReservationStatus.Released)
        {
            return Result.Failure(Error.Conflict(
                "promotions.coupon.reservation_released",
                "Cette réservation a été libérée et ne peut plus être engagée."));
        }

        if (HasExpired(nowUtc))
        {
            // On refuse au lieu de prolonger. Le budget a pu être rendu et
            // réattribué entre-temps : engager ici dépenserait deux fois la même
            // enveloppe.
            return Result.Failure(Error.BusinessRule(
                "promotions.coupon.reservation_expired", "Cette réservation a expiré."));
        }

        OrderId = orderId;
        CommittedAtUtc = nowUtc;
        Status = CouponReservationStatus.Committed;
        return Result.Success();
    }

    internal void Release()
    {
        if (Status == CouponReservationStatus.Held)
        {
            Status = CouponReservationStatus.Released;
        }
    }

    /// <summary>
    /// Annule un usage DÉJÀ ENGAGÉ, parce que la commande a été annulée.
    ///
    /// DISTINCT DE <see cref="Release"/>, ET LE MÉLANGE SERAIT COÛTEUX.
    ///
    /// `Release` traite une retenue qui n'a jamais abouti — un panier abandonné.
    /// Ici la commande a bien été payée, puis annulée : il faut rendre au client
    /// son droit d'utiliser le coupon ET rendre à la campagne le budget engagé.
    /// Autoriser `Release` sur un usage engagé aurait fait de l'expiration d'une
    /// retenue un moyen d'effacer un usage payé.
    /// </summary>
    internal void Revoke()
    {
        if (Status == CouponReservationStatus.Committed)
        {
            Status = CouponReservationStatus.Released;
        }
    }
}
