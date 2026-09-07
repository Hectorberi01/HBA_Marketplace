namespace HBA.Promotions.Domain.Promotions;

/// <summary>Univers auquel une promotion s'applique (§10.16, colonne <c>scope</c>).</summary>
public enum PromotionScope
{
    Global = 0,
    Marketplace = 1,
    Food = 2
}

/// <summary>Nature de la remise (§10.16, colonne <c>type</c>).</summary>
public enum PromotionType
{
    /// <summary>Pourcentage du sous-total.</summary>
    Percent = 0,

    /// <summary>Montant fixe en unités monétaires entières.</summary>
    Fixed = 1,

    /// <summary>Annule les frais de livraison, sans toucher au sous-total.</summary>
    FreeDelivery = 2
}

public enum PromotionStatus
{
    Draft = 0,
    Scheduled = 1,
    Active = 2,

    /// <summary>Budget consommé. Distinct de `Expired` : la cause n'est pas la même.</summary>
    Exhausted = 3,

    /// <summary>ÉTAT INATTEIGNABLE : RIEN NE FAIT EXPIRER UNE PROMOTION (lot 9.2).</summary>
    Expired = 4,

    Cancelled = 5
}

/// <summary>Contexte d'évaluation : ce que le panier apporte pour décider d'une remise.</summary>
public sealed record PromotionContext(
    PromotionScope Scope,
    long Subtotal,
    long DeliveryFee,
    string Currency,
    Guid UserId);

/// <summary>Remise calculée, décomposée pour que l'appelant sache quoi imputer.</summary>
public sealed record PromotionDiscount(long AmountOffSubtotal, long AmountOffDelivery)
{
    public static readonly PromotionDiscount None = new(0, 0);

    public long Total => AmountOffSubtotal + AmountOffDelivery;
}

/// <summary>QUI PAIE LA REMISE (ISSUE-052, décision D28).</summary>
public enum PromotionFunder
{
    /// <summary>Part vendeur nulle : la plateforme paie tout.</summary>
    Platform = 0,

    /// <summary>Part vendeur totale : le vendeur paie tout.</summary>
    Seller = 1,

    /// <summary>Part vendeur strictement entre les deux — remise cofinancée.</summary>
    Shared = 2
}

/// <summary>Les bornes de la part de financement, en POINTS DE BASE.</summary>
public static class PromotionFunding
{
    /// <summary>10 000 points de base = 100 %.</summary>
    public const int TotalBasisPoints = 10_000;

    /// <summary>Part vendeur nulle.</summary>
    public const int PlatformOnly = 0;

    /// <summary>Part vendeur totale.</summary>
    public const int SellerOnly = TotalBasisPoints;
}

/// <summary>Une remise décomposée par financeur, en unités monétaires entières (§2).</summary>
public sealed record FundedDiscount(long SellerAmount, long PlatformAmount)
{
    public static readonly FundedDiscount None = new(0, 0);

    public long Total => SellerAmount + PlatformAmount;
}
