using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Commerce.Contracts.IntegrationEvents;
using HBA.Commerce.Domain.Carts.Events;

namespace HBA.Commerce.Application.Carts.EventHandlers;

/// <summary>Publie l'IntegrationEvent « panier validé » (consommé par Ordering / analytics).</summary>
public sealed class CartCheckedOutDomainEventHandler : IDomainEventHandler<CartCheckedOutDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CartCheckedOutDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(CartCheckedOutDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CartCheckedOutIntegrationEvent { CartId = domainEvent.CartId, BuyerId = domainEvent.BuyerId },
            cancellationToken);
}
