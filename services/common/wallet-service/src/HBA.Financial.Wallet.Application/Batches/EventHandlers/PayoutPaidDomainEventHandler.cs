using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Domain.Batches.Events;

namespace HBA.Financial.Wallet.Application.Batches.EventHandlers;

/// <summary>Publie « reversement versé » (Notifications informe le vendeur, compta enregistre).</summary>
public sealed class PayoutPaidDomainEventHandler : IDomainEventHandler<PayoutPaidDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PayoutPaidDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(PayoutPaidDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new PayoutPaidIntegrationEvent
            {
                BatchId = domainEvent.BatchId,
                PayoutId = domainEvent.PayoutId,
                SellerId = domainEvent.SellerId,
                NetAmount = domainEvent.NetAmount,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}
