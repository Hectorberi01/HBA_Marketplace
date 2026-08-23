namespace HBA.Promotions.Domain.Promotions;

/// <summary>
/// Univers auquel une promotion s'applique (§10.16, colonne <c>scope</c>).
///
/// IL EST VÉRIFIÉ À L'ÉVALUATION, PAS SEULEMENT À LA CRÉATION.
///
/// Un coupon « -15 % sur les restaurants » appliqué à un panier Marketplace n'est
/// pas une erreur de saisie du client : c'est une fuite de budget que personne ne
/// remarque avant la clôture du mois.
/// </summary>
public enum PromotionScope
{
    Global = 0,
    Marketplace = 1,
    Food = 2
}

/// <summary>Nature de la remise (§10.16, colonne <c>type</c>).</summary>
public enum PromotionType
{
    /// <summary>Pourcentage du sous-total. `Value` vaut 15 pour 15 %.</summary>
    Percent = 0,

    /// <summary>Montant fixe en unités monétaires entières. `Value` vaut 1000 pour 1 000 XOF.</summary>
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

    /// <summary>
    /// ÉTAT INATTEIGNABLE : RIEN NE FAIT EXPIRER UNE PROMOTION (lot 9.2).
    ///
    /// `Exhausted` est bien posé — le budget se consomme — mais aucune tâche ne
    /// compare `ValidUntil` à l'heure courante. Une campagne dont la date de fin
    /// est passée reste donc `Active` : elle n'apparaît plus dans les écrans qui
    /// filtrent par date, et continue d'être ACCORDÉE par l'évaluation, qui teste
    /// le statut.
    ///
    /// Conservée : c'est le vocabulaire du balayeur manquant, et le commentaire
    /// d'`Exhausted` juste au-dessus explique pourquoi les deux causes doivent
    /// rester distinctes.
    /// </summary>
    Expired = 4,

    Cancelled = 5
}

/// <summary>
/// Contexte d'évaluation : ce que le panier apporte pour décider d'une remise.
///
/// LES MONTANTS SONT EN UNITÉS MONÉTAIRES ENTIÈRES (§2).
///
/// Le franc CFA n'a pas de sous-unité, et le cahier des charges impose des BIGINT :
/// `amount=8850` signifie 8 850 XOF. Un `decimal` ici rouvrirait la porte aux
/// arrondis que ce choix ferme — et un arrondi sur une remise en pourcentage,
/// répété un million de fois, n'est plus une erreur d'arrondi.
/// </summary>
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUI PAIE LA REMISE (ISSUE-052, décision D28).
///
/// RIEN, DANS `Promotion`, NE DISAIT QUI SUPPORTE LE COÛT.
///
/// Le reste de la plateforme suppose pourtant la distinction depuis toujours :
/// `PriceBreakdownDto` porte `SellerDiscount` **et** `PlatformDiscount`,
/// `OrderLineDraft` les deux aussi, et wallet calcule le gain du vendeur sur
/// `UnitBasePrice - SellerDiscount`. Brancher promotion-service sans financeur
/// aurait fait payer aux VENDEURS les coupons de la PLATEFORME — silencieusement,
/// par le calcul des gains, et découvert au premier relevé contesté.
///
/// CETTE ÉNUMÉRATION EST UNE LECTURE, PAS UN STOCKAGE.
///
/// Ce qui est persisté est <see cref="Promotion.SellerFundedShareBps"/> — une PART
/// en points de base. C'est la seule forme qui satisfasse l'exigence de D28 : « le
/// champ doit permettre d'exprimer plus tard une remise COFINANCÉE sans migration
/// supplémentaire ». Un booléen ou une énumération à deux valeurs ne le permet
/// pas — le jour où le commerce demande « moitié-moitié », il faut une colonne de
/// plus, donc une migration, donc un déploiement coordonné.
///
/// <see cref="Shared"/> n'est donc PAS une troisième politique à implémenter : il
/// nomme ce que la part exprime déjà. Le calcul, lui, traite les trois cas de la
/// même façon — voir <see cref="Promotion.SplitDiscount"/>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum PromotionFunder
{
    /// <summary>Part vendeur nulle : la plateforme paie tout.</summary>
    Platform = 0,

    /// <summary>Part vendeur totale : le vendeur paie tout.</summary>
    Seller = 1,

    /// <summary>Part vendeur strictement entre les deux — remise cofinancée.</summary>
    Shared = 2
}

/// <summary>
/// Les bornes de la part de financement, en POINTS DE BASE.
///
/// POINTS DE BASE ET NON POURCENTAGE ENTIER.
///
/// « 33 % » et « 33,33 % » ne se distinguent pas en pourcentage entier, et un
/// partage à trois parts est exactement le premier cas que le commerce demandera.
/// Un `decimal` aurait rouvert la porte aux arrondis que le §2 ferme pour les
/// montants ; un entier en dix-millièmes garde l'arithmétique exacte et laisse
/// deux décimales de marge.
/// </summary>
public static class PromotionFunding
{
    /// <summary>10 000 points de base = 100 %.</summary>
    public const int TotalBasisPoints = 10_000;

    /// <summary>Part vendeur nulle.</summary>
    public const int PlatformOnly = 0;

    /// <summary>Part vendeur totale.</summary>
    public const int SellerOnly = TotalBasisPoints;
}

/// <summary>
/// Une remise décomposée par financeur, en unités monétaires entières (§2).
///
/// LE RESTE D'ARRONDI VA À LA PLATEFORME, JAMAIS AU VENDEUR.
///
/// `SellerAmount + PlatformAmount` vaut EXACTEMENT la remise accordée : la
/// division entière ne perd rien, elle décide seulement qui absorbe l'unité
/// restante. La faire porter au vendeur produirait, sur un relevé mensuel, des
/// écarts d'un franc qu'aucune ligne n'explique — et c'est précisément le genre de
/// détail qui fait perdre confiance dans un relevé entier. Voir
/// <see cref="Promotion.SplitDiscount"/>.
/// </summary>
public sealed record FundedDiscount(long SellerAmount, long PlatformAmount)
{
    public static readonly FundedDiscount None = new(0, 0);

    public long Total => SellerAmount + PlatformAmount;
}
