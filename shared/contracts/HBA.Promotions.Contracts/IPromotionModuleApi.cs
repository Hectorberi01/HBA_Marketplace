namespace HBA.Promotions.Contracts;

/// <summary>Ce qu'un panier apporte pour qu'une remise soit calculée.</summary>
/// <param name="Scope">« GLOBAL », « MARKETPLACE » ou « FOOD ».</param>
/// <param name="Subtotal">Sous-total en unités monétaires entières (§2).</param>
/// <param name="DeliveryFee">Frais de livraison, séparés parce qu'une remise peut ne toucher qu'eux.</param>
public sealed record PromotionEvaluationContext(
    string Scope, long Subtotal, long DeliveryFee, string Currency, Guid UserId);

/// <summary>
/// Verdict d'une évaluation.
///
/// UN COUPON REFUSÉ N'EST PAS UNE ERREUR.
///
/// `Valid = false` avec une `Reason` est la réponse NORMALE à un code périmé ou
/// inapplicable : saisir un mauvais code est un usage ordinaire du champ. Lever
/// une exception gRPC ferait apparaître chaque frappe d'un client dans les
/// compteurs d'erreur du service — et, pire, ouvrirait le disjoncteur du côté
/// appelant, coupant les évaluations valides des autres clients.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// `Discount` SEUL NE DIT PAS QUI PAIE, ET C'ÉTAIT LE DÉFAUT (ISSUE-052, D28).
///
/// L'appelant reçoit un montant et doit écrire, dans son `PriceBreakdownDto`, un
/// `SellerDiscount` ET un `PlatformDiscount`. Avec un seul total, il n'a le choix
/// qu'entre deux erreurs :
///
///   • tout imputer au vendeur — et wallet, qui calcule le gain sur
///     `UnitBasePrice - SellerDiscount`, prélève sur des marchands qui n'ont rien
///     signé, par un chemin où le prélèvement ne se voit pas ;
///   • tout mettre à zéro — c'est ce que fait `NeutralPricingModuleApi`, et c'est
///     la raison pour laquelle aucune campagne n'existe.
///
/// LES TROIS CHAMPS SONT AJOUTÉS EN FIN, AVEC UN DÉFAUT (D32).
///
/// `scripts/check-event-contracts.py` ne surveille que les `*IntegrationEvent`,
/// mais la règle vaut pour tous les contrats de ce dépôt : on n'ajoute que de
/// l'OPTIONNEL ; une rupture crée un nouveau type. Un appelant déjà déployé
/// continue de construire ce record avec six arguments et continue de compiler.
///
/// L'INVARIANT À CONNAÎTRE.
///
/// Dès que `Valid` est vrai :
/// `SellerFundedDiscount + PlatformFundedDiscount == Discount`, exactement, en
/// unités monétaires entières. Le reste de la division entière est absorbé par la
/// PLATEFORME — voir `Promotion.SplitDiscount`. Un appelant qui recalculerait la
/// répartition depuis une part en pourcentage obtiendrait, une fois sur deux, un
/// franc de plus au vendeur : ces deux montants sont la vérité, pas une indication.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record PromotionEvaluationResult(
    bool Valid,
    Guid? PromotionId,
    long Discount,
    string Currency,
    string Message,
    string? Reason,

    /// <summary>Part de <c>Discount</c> supportée par le VENDEUR propriétaire.</summary>
    long SellerFundedDiscount = 0,

    /// <summary>Part de <c>Discount</c> supportée par la PLATEFORME.</summary>
    long PlatformFundedDiscount = 0,

    /// <summary>
    /// Le vendeur qui finance. <c>null</c> = campagne de la plateforme.
    ///
    /// INDISPENSABLE À L'IMPUTATION EN PANIER MULTI-VENDEURS : sans lui,
    /// `SellerFundedDiscount` serait imputé à TOUS les vendeurs du panier, donc à
    /// des marchands qui n'ont pas financé cette campagne. Voir
    /// `PromotionPricingModuleApi`.
    /// </summary>
    Guid? OwnerSellerId = null);

/// <summary>Une retenue accordée, à engager au paiement ou à laisser expirer.</summary>
public sealed record CouponReservationResult(
    Guid ReservationId, Guid CouponId, Guid PromotionId,
    long DiscountAmount, string Currency, DateTime ExpiresAtUtc);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'API DE PROMOTION POUR LES AUTRES SERVICES (§10.16, contrats gRPC).
///
/// ÉVALUER ET RÉSERVER SONT DEUX OPÉRATIONS, ET LES CONFONDRE COÛTE UN BUDGET.
///
/// `EvaluateAsync` est en LECTURE PURE. L'écran du panier la rappelle à chaque
/// changement de quantité ; si elle réservait, dix modifications consommeraient
/// dix fois l'enveloppe et épuiseraient une campagne sans qu'aucune commande ne
/// soit passée.
///
/// `ReserveAsync` engage le budget, et n'a sa place qu'au checkout.
///
/// LE COUPLE RÉSERVER / ENGAGER EST UNE SAGA, PAS UNE TRANSACTION.
///
/// Entre les deux, le paiement peut échouer et le processus appelant peut mourir.
/// C'est pourquoi une retenue EXPIRE d'elle-même : la compensation ne dépend pas
/// de la bonne volonté — ni de la survie — de celui qui l'a demandée. Un appelant
/// consciencieux appelle quand même `ReleaseAsync`, ce qui rend le budget tout de
/// suite au lieu d'attendre l'expiration.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IPromotionModuleApi
{
    /// <summary>Calcule la remise SANS rien consommer. Rejouable autant qu'on veut.</summary>
    Task<PromotionEvaluationResult> EvaluateAsync(
        string code, PromotionEvaluationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retient le coupon pour ce panier et consomme le budget.
    ///
    /// Idempotent par panier : un second appel pour le même `cartId` rend la MÊME
    /// retenue sans rien débiter de plus. C'est ce qui rend un double-clic, un
    /// rejeu réseau ou une reprise de checkout inoffensifs.
    /// </summary>
    Task<CouponReservationResult?> ReserveAsync(
        string code, Guid userId, Guid cartId, PromotionEvaluationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Engage la retenue : la commande est payée, l'usage devient définitif.
    ///
    /// Idempotent — Kafka livre au moins une fois, et un rejeu ne doit pas compter
    /// un second usage.
    /// </summary>
    Task<bool> CommitAsync(
        Guid reservationId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Libère une retenue non engagée et rend le budget immédiatement.
    ///
    /// Facultatif au sens strict : l'expiration finit par le faire. Mais attendre
    /// trente minutes pour rendre du budget sur un checkout abandonné en dix
    /// secondes immobilise une enveloppe pour rien.
    /// </summary>
    Task<bool> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default);
}
