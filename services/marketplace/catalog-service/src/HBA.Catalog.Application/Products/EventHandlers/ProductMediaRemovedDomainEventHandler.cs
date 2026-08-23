using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Catalog.Domain.Products.Events;

namespace HBA.Catalog.Application.Products.EventHandlers;

/// <summary>
/// Frontière domaine → intégration : porte hors de Catalog le nom du fichier à
/// effacer. Sans ce relais, l'événement de domaine mourrait dans le module et
/// l'image resterait dans le stockage.
/// </summary>
public sealed class ProductMediaRemovedDomainEventHandler : IDomainEventHandler<ProductMediaRemovedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductMediaRemovedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductMediaRemovedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ProductMediaRemovedIntegrationEvent
            {
                ProductId = domainEvent.ProductId,
                MediaId = domainEvent.MediaId
            },
            cancellationToken);
}
