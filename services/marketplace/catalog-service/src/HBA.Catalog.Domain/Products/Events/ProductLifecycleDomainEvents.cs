using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Products.Events;

/// <summary>LES HUIT FAITS DU CYCLE DE VIE (§19).</summary>
public sealed record ProductSubmittedForReviewDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    int RevisionVersion) : DomainEvent;

public sealed record ProductApprovedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    Guid ReviewedBy) : DomainEvent;

/// <summary>
/// Les motifs ne voyagent pas ici : ils vivent dans ProductReview, et un rejet en
/// porte plusieurs, chacun avec un champ visé (§16).
/// </summary>
public sealed record ProductRejectedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    Guid ReviewedBy) : DomainEvent;

/// <summary>CET ÉVÉNEMENT NOMME LA RÉVISION, PAS SEULEMENT LE PRODUIT.</summary>
public sealed record ProductPublishedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    Guid? PreviousRevisionId) : DomainEvent;

public sealed record ProductUnpublishedDomainEvent(
    Guid ProductId,
    Guid SellerId) : DomainEvent;

/// <summary>Retrait par la plateforme.</summary>
public sealed record ProductSuspendedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    string? Reason) : DomainEvent;

public sealed record ProductRestoredDomainEvent(
    Guid ProductId,
    Guid SellerId) : DomainEvent;

public sealed record ProductArchivedDomainEvent(
    Guid ProductId,
    Guid SellerId) : DomainEvent;

/// <summary>Une nouvelle révision est ouverte sur un produit DÉJÀ PUBLIÉ (§6).</summary>
public sealed record ProductRevisionOpenedDomainEvent(
    Guid ProductId,
    Guid RevisionId,
    int RevisionVersion,
    Guid? PublishedRevisionId) : DomainEvent;
