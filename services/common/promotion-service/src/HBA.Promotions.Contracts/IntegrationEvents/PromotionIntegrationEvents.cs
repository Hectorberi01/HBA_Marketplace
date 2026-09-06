using HBA.Shared.IntegrationEvents;

namespace HBA.Promotions.Contracts.IntegrationEvents;

/// <summary>
/// Une campagne vient d'être créée (§10.16, <c>promotion.created</c>).
///
/// CET ÉVÉNEMENT N'AUTORISE PERSONNE À APPLIQUER LA REMISE.
///
/// Il annonce qu'une campagne EXISTE — de quoi alimenter un écran d'administration
/// ou un tableau de bord marketing. L'éligibilité d'un panier donné se demande à
/// `EvaluatePromotion`, parce qu'elle dépend de conditions que cet événement ne
/// transporte pas et ne doit pas transporter : un consommateur qui recopierait les
/// règles pour décider lui-même les verrait diverger au premier ajout.
/// </summary>
[HbaEvent("promotion.created", Version = 1, AggregateType = "Promotion")]
public sealed record PromotionCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid PromotionId { get; init; }

    public required string Name { get; init; }

    /// <summary>« GLOBAL », « MARKETPLACE » ou « FOOD ».</summary>
    public required string Scope { get; init; }

    /// <summary>« PERCENT », « FIXED » ou « FREE_DELIVERY ».</summary>
    public required string Type { get; init; }

    /// <summary>15 pour 15 %, ou un montant en unités entières (§2).</summary>
    public required long Value { get; init; }

    public required DateTime StartsAtUtc { get; init; }

    public required DateTime EndsAtUtc { get; init; }

    /// <summary>Null = pas de plafond global.</summary>
    public long? Budget { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Part de la remise supportée par le VENDEUR, en points de base (D28).
    /// 0 = la plateforme paie tout ; 10 000 = le vendeur paie tout.
    /// </summary>
    /// <remarks>
    /// OPTIONNEL, ET C'EST LA RÈGLE D32, PAS UNE HÉSITATION.
    ///
    /// Un champ ajouté en `required` sur un contrat déjà publié serait rempli à
    /// `null` par tout producteur pas encore redéployé, et le consommateur
    /// écrirait un effet faux sans qu'aucune erreur ne parte. Une rupture crée un
    /// NOUVEAU type d'événement ; un enrichissement s'ajoute en optionnel.
    ///
    /// Un consommateur qui lit `null` doit donc lire « ce producteur est antérieur
    /// à D28 », et non « la plateforme paie » — la distinction compte le jour où
    /// quelqu'un facturera sur cette base.
    /// </remarks>
    public int? SellerFundedShareBps { get; init; }

    /// <summary>Vendeur propriétaire de la campagne. <c>null</c> = la plateforme.</summary>
    public Guid? OwnerSellerId { get; init; }
}

/// <summary>
/// Le budget d'une campagne est consommé (§10.16, <c>promotion.exhausted</c>).
///
/// C'EST UNE ALERTE POUR UN HUMAIN, PAS UN SIGNAL DE MACHINE.
///
/// Elle demande une décision — remettre au budget, ou laisser la campagne mourir.
/// Le domaine ne la publie donc qu'à la TRANSITION : une fois épuisée, toute
/// tentative de réservation suivante retombe sur « budget insuffisant », et sans
/// cette garde une campagne populaire en émettrait des dizaines par minute.
/// </summary>
[HbaEvent("promotion.exhausted", Version = 1, AggregateType = "Promotion")]
public sealed record PromotionExhaustedIntegrationEvent : IntegrationEvent
{
    public required Guid PromotionId { get; init; }

    public required string Name { get; init; }

    public required long BudgetConsumed { get; init; }
}

/// <summary>
/// Un coupon a été engagé sur une commande payée (§10.16, <c>coupon.used</c>).
///
/// ENGAGÉ, PAS RÉSERVÉ — ET LA NUANCE VAUT DE L'ARGENT.
///
/// Une réservation est provisoire : elle expire, se libère, et une bonne part des
/// paniers n'aboutit jamais. Publier à la réservation ferait compter au marketing
/// des usages qui n'ont jamais eu lieu, et la remise annoncée ne correspondrait à
/// aucune ligne comptable.
///
/// Il n'existe volontairement PAS de `coupon.released` symétrique : l'annulation
/// d'une commande est déjà annoncée par `marketplace.order.cancelled` et
/// `food.order.cancelled`, que ce service consomme. Republier la compensation
/// obligerait chaque consommateur à corréler deux événements pour reconstituer un
/// seul fait.
/// </summary>
[HbaEvent("coupon.used", Version = 1, AggregateType = "Coupon")]
public sealed record CouponUsedIntegrationEvent : IntegrationEvent
{
    public required Guid CouponId { get; init; }

    public required Guid PromotionId { get; init; }

    /// <summary>
    /// Le code saisi, normalisé en majuscules.
    ///
    /// Transporté parce que c'est le seul identifiant qu'un humain reconnaît : une
    /// réclamation arrive sous la forme « WELCOME10 n'a pas marché », jamais sous
    /// celle d'un UUID.
    /// </summary>
    public required string Code { get; init; }

    public required Guid UserId { get; init; }

    public required Guid OrderId { get; init; }

    /// <summary>Montant réellement accordé, en unités monétaires entières (§2).</summary>
    public required long DiscountAmount { get; init; }
}
