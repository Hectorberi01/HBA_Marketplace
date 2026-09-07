using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Merchants.Domain.Stores.Events;

namespace HBA.Merchants.Application.Stores;

/// <summary>
/// Publie « boutique fermée » — c'est ce message qui retire ses offres de la vente.
/// </summary>
public sealed class StoreClosedDomainEventHandler : IDomainEventHandler<StoreClosedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StoreClosedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StoreClosedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StoreClosedIntegrationEvent
            {
                StoreId = domainEvent.StoreId,
                SellerId = domainEvent.SellerId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie « boutique ouverte ».</summary>
public sealed class StoreOpenedDomainEventHandler : IDomainEventHandler<StoreOpenedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StoreOpenedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StoreOpenedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StoreOpenedIntegrationEvent
            {
                StoreId = domainEvent.StoreId,
                SellerId = domainEvent.SellerId
            },
            cancellationToken);
}

/// <summary>
/// Publie « boutique suspendue » — distinct de « boutique fermée », et c'est tout
/// l'intérêt : une sanction et des congés partaient jusqu'ici sous le même type.
/// </summary>
public sealed class StoreSuspendedDomainEventHandler : IDomainEventHandler<StoreSuspendedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StoreSuspendedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(StoreSuspendedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StoreSuspendedIntegrationEvent
            {
                StoreId = domainEvent.StoreId,
                SellerId = domainEvent.SellerId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie « sanction levée ».</summary>
public sealed class StoreSuspensionLiftedDomainEventHandler
    : IDomainEventHandler<StoreSuspensionLiftedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public StoreSuspensionLiftedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        StoreSuspensionLiftedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new StoreSuspensionLiftedIntegrationEvent
            {
                StoreId = domainEvent.StoreId,
                SellerId = domainEvent.SellerId
            },
            cancellationToken);
}
