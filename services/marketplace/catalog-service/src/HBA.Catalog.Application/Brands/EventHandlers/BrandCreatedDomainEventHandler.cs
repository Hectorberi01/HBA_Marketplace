using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Catalog.Domain.Brands.Events;

namespace HBA.Catalog.Application.Brands.EventHandlers;

public sealed class BrandCreatedDomainEventHandler : IDomainEventHandler<BrandCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public BrandCreatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(BrandCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new BrandCreatedIntegrationEvent
            {
                BrandId = domainEvent.BrandId,
                Name = domainEvent.Name,
                Slug = domainEvent.Slug
            },
            cancellationToken);
}
