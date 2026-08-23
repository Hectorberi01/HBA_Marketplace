using HBA.Promotions.Contracts;
using HBA.Promotions.Contracts.IntegrationEvents;
using HBA.Promotions.Domain.Promotions.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Promotions.Application.Promotions;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CHAÎNON ENTRE LE DOMAINE ET KAFKA.
///
/// ÉCRIT EN MÊME TEMPS QUE LES ÉVÉNEMENTS, ET CE N'EST PAS UN HASARD.
///
/// media-service levait ses trois événements de domaine depuis l'origine, avec un
/// commentaire affirmant qu'ils passaient par l'outbox — et personne ne les
/// traduisait. Le service compilait, les tests passaient, et rien ne sortait du
/// processus. Le défaut n'a été trouvé qu'en auditant le service un an plus tard.
///
/// L'inscription de ces trois classes dans `PromotionsModuleInstaller` est
/// manuelle et rien dans le compilateur ne la rappelle : c'est le point exact où
/// la même panne peut se reproduire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class PromotionCreatedDomainEventHandler
    : IDomainEventHandler<PromotionCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PromotionCreatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        PromotionCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PromotionCreatedIntegrationEvent
            {
                PromotionId = domainEvent.PromotionId,
                Name = domainEvent.Name,

                // LE CONTRAT PUBLIC N'EST PAS `Enum.ToString()`.
                //
                // Le §10.16 écrit `FOOD|MARKETPLACE|GLOBAL` et `PERCENT|FIXED|
                // FREE_DELIVERY` ; l'énumération C# rend « Food » et
                // « FreeDelivery ». La conversion vit dans le projet de contrat —
                // voir `PromotionConstantes` — et non ici : enfouie dans cette
                // classe interne, elle n'aurait été testable que par l'événement
                // produit, donc jamais sur ses cas limites.
                Scope = PromotionConstantes.Convertir(domainEvent.Scope),
                Type = PromotionConstantes.Convertir(domainEvent.Type),
                Value = domainEvent.Value,
                StartsAtUtc = domainEvent.StartsAtUtc,
                EndsAtUtc = domainEvent.EndsAtUtc,
                Budget = domainEvent.Budget,
                Currency = domainEvent.Currency,

                // QUI PAIE VOYAGE AVEC LA CAMPAGNE (D28).
                //
                // Les deux champs sont OPTIONNELS dans le contrat : un
                // consommateur déjà déployé les ignore et continue de fonctionner.
                // Sans eux, un tableau de bord marketing afficherait le budget
                // d'une campagne sans jamais dire de quelle poche il sort — et
                // c'est très exactement la question que D28 corrige.
                SellerFundedShareBps = domainEvent.SellerFundedShareBps,
                OwnerSellerId = domainEvent.OwnerSellerId
            },
            cancellationToken);
}

/// <summary>Publie l'alerte de budget épuisé. Voir la garde de `Promotion.Epuiser`.</summary>
internal sealed class PromotionExhaustedDomainEventHandler
    : IDomainEventHandler<PromotionExhaustedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PromotionExhaustedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        PromotionExhaustedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PromotionExhaustedIntegrationEvent
            {
                PromotionId = domainEvent.PromotionId,
                Name = domainEvent.Name,
                BudgetConsumed = domainEvent.BudgetConsumed
            },
            cancellationToken);
}

/// <summary>Publie l'usage engagé d'un coupon.</summary>
internal sealed class CouponUsedDomainEventHandler : IDomainEventHandler<CouponUsedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CouponUsedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        CouponUsedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CouponUsedIntegrationEvent
            {
                CouponId = domainEvent.CouponId,
                PromotionId = domainEvent.PromotionId,
                Code = domainEvent.Code,
                UserId = domainEvent.UserId,
                OrderId = domainEvent.OrderId,
                DiscountAmount = domainEvent.DiscountAmount
            },
            cancellationToken);
}
