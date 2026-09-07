using HBA.Shared.Domain.Events;

namespace HBA.Engagement.Reviews.Domain.Reviews.Events;

/// <summary>Un avis a été publié.</summary>
public sealed record ReviewPublishedDomainEvent(
    Guid ReviewId, Guid ProductId, Guid SellerId, int Rating) : DomainEvent;

/// <summary>Un avis a été retiré (rejeté) — sa contribution à la note disparaît.</summary>
public sealed record ReviewRejectedDomainEvent(
    Guid ReviewId, Guid ProductId, Guid SellerId) : DomainEvent;
