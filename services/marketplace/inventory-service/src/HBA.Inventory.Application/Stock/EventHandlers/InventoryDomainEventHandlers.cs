using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Inventory.Domain.Stock.Events;

namespace HBA.Inventory.Application.Stock.EventHandlers;

/// <summary>Publie l'IntegrationEvent « stock réservé » (consommé par Ordering).</summary>
public sealed class StockReservedDomainEventHandler : IDomainEventHandler<StockReservedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StockReservedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StockReservedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StockReservedIntegrationEvent
            {
                InventoryItemId = domainEvent.InventoryItemId,
                Sku = domainEvent.Sku,
                OrderId = domainEvent.OrderId,
                Quantity = domainEvent.Quantity
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « rupture de stock » (consommé par Offers, Search).</summary>
public sealed class StockDepletedDomainEventHandler : IDomainEventHandler<StockDepletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StockDepletedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StockDepletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StockDepletedIntegrationEvent
            {
                InventoryItemId = domainEvent.InventoryItemId,
                Sku = domainEvent.Sku,
                LocationId = domainEvent.LocationId
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « stock reconstitué » (consommé par Products).</summary>
public sealed class StockReplenishedDomainEventHandler : IDomainEventHandler<StockReplenishedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StockReplenishedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StockReplenishedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StockReplenishedIntegrationEvent
            {
                InventoryItemId = domainEvent.InventoryItemId,
                Sku = domainEvent.Sku,
                LocationId = domainEvent.LocationId
            },
            cancellationToken);
}
