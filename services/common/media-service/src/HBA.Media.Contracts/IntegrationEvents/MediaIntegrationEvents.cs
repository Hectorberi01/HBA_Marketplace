using HBA.Shared.IntegrationEvents;

namespace HBA.Media.Contracts.IntegrationEvents;

/// <summary>LES ÉVÉNEMENTS DU SERVICE MÉDIA.</summary>
[HbaEvent("media.ready", Version = 1, AggregateType = "MediaAsset")]
public sealed record MediaReadyIntegrationEvent : IntegrationEvent
{
    public required Guid MediaId { get; init; }

    /// <summary>« Product », « Seller », « Delivery »… — voir `MediaOwnerType`.</summary>
    public required string OwnerType { get; init; }

    public required Guid OwnerId { get; init; }

    /// <summary>La NATURE du fichier : « ProductImage », « DriverDocument »…</summary>
    public required string MediaType { get; init; }

    /// <summary>La clé de stockage de l'ORIGINAL.</summary>
    public required string ObjectKey { get; init; }
}

/// <summary>Le fichier est supprimé LOGIQUEMENT.</summary>
[HbaEvent("media.deleted", Version = 1, AggregateType = "MediaAsset")]
public sealed record MediaDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid MediaId { get; init; }

    public required string OwnerType { get; init; }

    public required Guid OwnerId { get; init; }

    public required string MediaType { get; init; }
}

/// <summary>La génération des variantes a échoué.</summary>
[HbaEvent("media.processing_failed", Version = 1, AggregateType = "MediaAsset")]
public sealed record MediaProcessingFailedIntegrationEvent : IntegrationEvent
{
    public required Guid MediaId { get; init; }

    public required string OwnerType { get; init; }

    public required Guid OwnerId { get; init; }

    public required string Reason { get; init; }
}
