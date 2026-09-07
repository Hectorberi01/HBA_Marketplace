using HBA.Marketplace.ReturnRefund.Domain.Events;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Marketplace.ReturnRefund.Application.Events;

/// <summary>LES DEUX ÉVÉNEMENTS DU MODULE RETOURS N'ÉTAIENT PUBLIÉS PAR PERSONNE.</summary>
public sealed class RefundRequestedDomainEventHandler : IDomainEventHandler<RefundRequestedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RefundRequestedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(RefundRequestedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReturnRefundApprovedIntegrationEvent
            {
                ReturnRequestId = domainEvent.ReturnId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.CustomerId,
                SellerId = domainEvent.SellerId,
                RefundAmount = domainEvent.Amount,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>
/// L'argent est parti : wallet-service contre-passe, notification-service prévient.
/// </summary>
public sealed class RefundSucceededDomainEventHandler : IDomainEventHandler<RefundSucceededDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RefundSucceededDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(RefundSucceededDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReturnRefundedIntegrationEvent
            {
                ReturnRequestId = domainEvent.ReturnId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.CustomerId,
                SellerId = domainEvent.SellerId,
                RefundAmount = domainEvent.Amount,
                Currency = domainEvent.Currency,
                RefundReference = domainEvent.ProviderRefundId,

                // CES DEUX CHAMPS SONT LE VOLET RETURN-REFUND D'ISSUE-014.
                Lines = domainEvent.Lines
                    .Select(l => new ReturnedOrderLine { OrderItemId = l.OrderItemId, Quantity = l.Quantity })
                    .ToList(),
                ReturnTotalRefundedAmount = domainEvent.ReturnTotalRefunded
            },
            cancellationToken);
}
