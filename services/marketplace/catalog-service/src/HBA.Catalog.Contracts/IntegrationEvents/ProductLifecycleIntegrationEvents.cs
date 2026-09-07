using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

// LES ÉVÉNEMENTS DU CYCLE DE VIE PRODUIT (§19).

/// <summary>Le vendeur a soumis une révision à validation (§15).</summary>
[HbaEvent("catalog", "product", "submitted", Version = 1, AggregateType = "Product")]
public sealed record ProductSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public required int RevisionVersion { get; init; }
}

/// <summary>Un administrateur a validé la révision (§16).</summary>
[HbaEvent("catalog", "product", "approved", Version = 1, AggregateType = "Product")]
public sealed record ProductApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public required Guid ReviewedBy { get; init; }
}

/// <summary>Rejet motivé (§16).</summary>
[HbaEvent("catalog", "product", "rejected", Version = 1, AggregateType = "Product")]
public sealed record ProductRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public required Guid ReviewedBy { get; init; }
}

/// <summary>La fiche est visible dans la marketplace.</summary>
[HbaEvent("catalog", "product", "published", Version = 1, AggregateType = "Product")]
public sealed record ProductPublishedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public Guid? PreviousRevisionId { get; init; }
}

/// <summary>Retrait volontaire par le vendeur.</summary>
[HbaEvent("catalog", "product", "unpublished", Version = 1, AggregateType = "Product")]
public sealed record ProductUnpublishedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>Retrait par la plateforme.</summary>
[HbaEvent("catalog", "product", "suspended", Version = 1, AggregateType = "Product")]
public sealed record ProductSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Suspension levée : la fiche revient à APPROVED, pas à PUBLISHED.</summary>
[HbaEvent("catalog", "product", "restored", Version = 1, AggregateType = "Product")]
public sealed record ProductRestoredIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>Retrait définitif du cycle courant.</summary>
[HbaEvent("catalog", "product", "archived", Version = 1, AggregateType = "Product")]
public sealed record ProductArchivedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}
