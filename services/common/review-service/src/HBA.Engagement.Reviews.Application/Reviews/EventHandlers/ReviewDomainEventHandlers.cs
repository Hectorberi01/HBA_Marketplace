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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// RECALCULE LA NOTE DU VENDEUR ET PUBLIE LE RÉSULTAT.
    ///
    /// ON PUBLIE LA MOYENNE, PAS L'AVIS QUI VIENT D'ARRIVER.
    ///
    /// Laisser seller-service accumuler les notes reçues le ferait double-compter
    /// au premier rejeu — Kafka livre au moins une fois. Recalculer ici, depuis nos
    /// propres tables, et poser le résultat rend le consommateur idempotent sans
    /// qu'il ait à s'en occuper.
    ///
    /// ET C'EST NOUS QUI RECALCULONS, PAS LE CONSOMMATEUR.
    ///
    /// L'alternative — seller-service nous rappelle pour la moyenne — supposerait
    /// un client gRPC vers ce service. `HBA.Engagement.Contracts.Grpc` ne contient
    /// qu'un `.csproj` : le proto y est déclaré, `GetSellerRating` y figure, aucune
    /// classe n'est écrite et personne ne référence le projet. Construire cette
    /// couche pour un seul appelant coûterait plus cher que de porter trois nombres
    /// dans un événement.
    ///
    /// ET CETTE LECTURE N'EST PAS MISE EN CACHE, VOLONTAIREMENT.
    ///
    /// `ReviewQueries` explique que la note vendeur ne l'est pas justement parce
    /// qu'elle est calculée à chaque AVIS, pas à chaque AFFICHAGE. C'est ici que
    /// cette hypothèse cesse d'être théorique.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

/// <summary>
/// Publie « avis rejeté », et fait recalculer la note du vendeur.
/// </summary>
/// <remarks>
/// LE REJET COMPTE AUTANT QUE LA PUBLICATION.
///
/// `GetSellerRatingAsync` ne retient que les avis `Published` : retirer un avis
/// change donc la moyenne. Ne republier qu'à la publication laisserait un vendeur
/// porter indéfiniment la note d'un avis modéré — et ce serait le seul endroit où
/// le retrait d'un avis ne se verrait nulle part.
/// </remarks>
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
