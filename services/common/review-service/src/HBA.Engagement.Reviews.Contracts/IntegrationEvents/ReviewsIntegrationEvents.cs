using HBA.Shared.IntegrationEvents;

namespace HBA.Engagement.Reviews.Contracts.IntegrationEvents;

/// <summary>Un avis a été publié.</summary>
[HbaEvent("engagement.review.published")]
public sealed record ReviewPublishedIntegrationEvent : IntegrationEvent
{
    public required Guid ReviewId { get; init; }
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required int Rating { get; init; }
}

/// <summary>Un avis a été rejeté : sa contribution aux notes disparaît.</summary>
[HbaEvent("engagement.review.rejected")]
public sealed record ReviewRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid ReviewId { get; init; }
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>LA NOTE D'UN VENDEUR VIENT D'ÊTRE RECALCULÉE. VOICI SA VALEUR.</summary>
[HbaEvent("engagement.seller.rating.recomputed")]
public sealed record SellerRatingRecomputedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    /// <summary>Moyenne des avis publiés, arrondie à deux décimales.</summary>
    public required double Average { get; init; }

    /// <summary>Nombre d'avis publiés retenus dans la moyenne.</summary>
    public required int Count { get; init; }
}
