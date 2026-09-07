using HBA.Promotions.Contracts;
using HBA.Promotions.Contracts.IntegrationEvents;
using HBA.Promotions.Domain.Promotions.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Promotions.Application.Promotions;

/// <summary>LE CHAÎNON ENTRE LE DOMAINE ET KAFKA.</summary>
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
                Scope = PromotionConstantes.Convertir(domainEvent.Scope),
                Type = PromotionConstantes.Convertir(domainEvent.Type),
                Value = domainEvent.Value,
                StartsAtUtc = domainEvent.StartsAtUtc,
                EndsAtUtc = domainEvent.EndsAtUtc,
                Budget = domainEvent.Budget,
                Currency = domainEvent.Currency,

                // QUI PAIE VOYAGE AVEC LA CAMPAGNE (D28).
                SellerFundedShareBps = domainEvent.SellerFundedShareBps,
                OwnerSellerId = domainEvent.OwnerSellerId
            },
            cancellationToken);
}

/// <summary>Publie l'alerte de budget épuisé.</summary>
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
