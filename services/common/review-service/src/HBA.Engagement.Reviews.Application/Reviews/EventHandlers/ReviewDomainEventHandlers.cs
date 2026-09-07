using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Engagement.Reviews.Domain.Reviews;
using HBA.Engagement.Reviews.Domain.Reviews.Events;

namespace HBA.Engagement.Reviews.Application.Reviews.EventHandlers;

/// <summary>Publie « avis publié », et fait recalculer la note du vendeur.</summary>
public sealed class ReviewPublishedDomainEventHandler : IDomainEventHandler<ReviewPublishedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IReviewRepository _reviews;

    public ReviewPublishedDomainEventHandler(
        IIntegrationEventPublisher publisher, IReviewRepository reviews)
    {
        _publisher = publisher;
        _reviews = reviews;
    }

    public async Task HandleAsync(
        ReviewPublishedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await _publisher.PublishAsync(
            new ReviewPublishedIntegrationEvent
            {
                ReviewId = domainEvent.ReviewId,
                ProductId = domainEvent.ProductId,
                SellerId = domainEvent.SellerId,
                Rating = domainEvent.Rating
            },
            cancellationToken);

        await PublierNoteVendeurAsync(_publisher, _reviews, domainEvent.SellerId, cancellationToken);
    }

    /// <summary>RECALCULE LA NOTE DU VENDEUR ET PUBLIE LE RÉSULTAT.</summary>
    internal static async Task PublierNoteVendeurAsync(
        IIntegrationEventPublisher publisher,
        IReviewRepository reviews,
        Guid sellerId,
        CancellationToken cancellationToken)
    {
        var note = await reviews.GetSellerRatingAsync(sellerId, cancellationToken);

        await publisher.PublishAsync(
            new SellerRatingRecomputedIntegrationEvent
            {
                SellerId = sellerId,
                Average = note.Average,
                Count = note.Count
            },
            cancellationToken);
    }
}

/// <summary>Publie « avis rejeté », et fait recalculer la note du vendeur.</summary>
public sealed class ReviewRejectedDomainEventHandler : IDomainEventHandler<ReviewRejectedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IReviewRepository _reviews;

    public ReviewRejectedDomainEventHandler(
        IIntegrationEventPublisher publisher, IReviewRepository reviews)
    {
        _publisher = publisher;
        _reviews = reviews;
    }

    public async Task HandleAsync(
        ReviewRejectedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await _publisher.PublishAsync(
            new ReviewRejectedIntegrationEvent
            {
                ReviewId = domainEvent.ReviewId,
                ProductId = domainEvent.ProductId,
                SellerId = domainEvent.SellerId
            },
            cancellationToken);

        await ReviewPublishedDomainEventHandler.PublierNoteVendeurAsync(
            _publisher, _reviews, domainEvent.SellerId, cancellationToken);
    }
}
