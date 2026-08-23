using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Catalog.Domain.Categories.Events;

namespace HBA.Catalog.Application.Categories.EventHandlers;

public sealed class CategoryCreatedDomainEventHandler : IDomainEventHandler<CategoryCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CategoryCreatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(CategoryCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CategoryCreatedIntegrationEvent
            {
                CategoryId = domainEvent.CategoryId,
                ParentId = domainEvent.ParentId,
                Name = domainEvent.Name,
                Slug = domainEvent.Slug,
                Path = domainEvent.Path
            },
            cancellationToken);
}
