using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Catalog.Domain.Products.Events;

namespace HBA.Catalog.Application.Products.EventHandlers;

// FRONTIÈRE DOMAINE → INTÉGRATION, POUR LES HUIT FAITS DU CYCLE DE VIE (§19).

public sealed class ProductSubmittedDomainEventHandler
    : IDomainEventHandler<ProductSubmittedForReviewDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductSubmittedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductSubmittedForReviewDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductSubmittedIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId,
            RevisionId = e.RevisionId,
            RevisionVersion = e.RevisionVersion
        }, cancellationToken);
}

public sealed class ProductApprovedDomainEventHandler
    : IDomainEventHandler<ProductApprovedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductApprovedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductApprovedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductApprovedIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId,
            RevisionId = e.RevisionId,
            ReviewedBy = e.ReviewedBy
        }, cancellationToken);
}

public sealed class ProductRejectedDomainEventHandler
    : IDomainEventHandler<ProductRejectedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductRejectedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductRejectedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductRejectedIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId,
            RevisionId = e.RevisionId,
            ReviewedBy = e.ReviewedBy
        }, cancellationToken);
}

public sealed class ProductPublishedDomainEventHandler
    : IDomainEventHandler<ProductPublishedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductPublishedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductPublishedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductPublishedIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId,
            RevisionId = e.RevisionId,
            PreviousRevisionId = e.PreviousRevisionId
        }, cancellationToken);
}

public sealed class ProductUnpublishedDomainEventHandler
    : IDomainEventHandler<ProductUnpublishedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductUnpublishedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductUnpublishedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductUnpublishedIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId
        }, cancellationToken);
}

public sealed class ProductSuspendedDomainEventHandler
    : IDomainEventHandler<ProductSuspendedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductSuspendedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductSuspendedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductSuspendedIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId,
            Reason = e.Reason
        }, cancellationToken);
}

public sealed class ProductRestoredDomainEventHandler
    : IDomainEventHandler<ProductRestoredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductRestoredDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductRestoredDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductRestoredIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId
        }, cancellationToken);
}

public sealed class ProductArchivedDomainEventHandler
    : IDomainEventHandler<ProductArchivedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public ProductArchivedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(ProductArchivedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new ProductArchivedIntegrationEvent
        {
            ProductId = e.ProductId,
            SellerId = e.SellerId
        }, cancellationToken);
}
