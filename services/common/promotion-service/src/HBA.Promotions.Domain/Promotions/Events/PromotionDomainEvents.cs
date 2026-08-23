using HBA.Shared.Domain.Events;

namespace HBA.Promotions.Domain.Promotions.Events;

/// <summary>
/// Une campagne vient d'être créée (§10.16, <c>promotion.created</c>).
///
/// LES DEUX DERNIERS CHAMPS ONT UN DÉFAUT, ET C'EST DE LA COMPATIBILITÉ, PAS
/// DE LA COMMODITÉ.
///
/// Ils ont été ajoutés par le lot D28. Leur donner un défaut garde compilables
/// les constructions existantes — les tests de domaine, notamment — sans quoi
/// l'ajout d'un champ à un événement obligerait à réécrire chaque site qui le
/// fabrique, y compris ceux qui ne s'y intéressent pas. Le défaut retenu est
/// « la plateforme paie », le même que partout ailleurs dans ce lot.
/// </summary>
public sealed record PromotionCreatedDomainEvent(
    Guid PromotionId, string Name, string Scope, string Type, long Value,
    DateTime StartsAtUtc, DateTime EndsAtUtc, long? Budget, string Currency,
    int SellerFundedShareBps = 0, Guid? OwnerSellerId = null) : DomainEvent;

/// <summary>
/// Le budget est consommé (§10.16, <c>promotion.exhausted</c>).
///
/// PUBLIÉ À LA TRANSITION, PAS À CHAQUE REFUS.
///
/// Une fois la campagne épuisée, toute tentative de réservation suivante retombe
/// sur « budget insuffisant ». Sans garde, une campagne populaire publierait des
/// dizaines d'événements par minute, et l'alerte qui doit déclencher une décision
/// humaine deviendrait du bruit qu'on filtre. Voir <c>Promotion.Epuiser</c>.
/// </summary>
public sealed record PromotionExhaustedDomainEvent(
    Guid PromotionId, string Name, long BudgetConsumed) : DomainEvent;

/// <summary>
/// Un coupon vient d'être engagé sur une commande payée (§10.16, <c>coupon.used</c>).
///
/// ENGAGÉ, PAS RÉSERVÉ.
///
/// Une réservation est provisoire : elle expire, se libère, et la moitié des
/// paniers n'aboutit jamais. Publier à la réservation ferait compter au marketing
/// des usages qui n'ont jamais eu lieu, et la remise annoncée ne correspondrait à
/// aucune ligne comptable.
/// </summary>
public sealed record CouponUsedDomainEvent(
    Guid CouponId, Guid PromotionId, string Code, Guid UserId, Guid OrderId, long DiscountAmount) : DomainEvent;
