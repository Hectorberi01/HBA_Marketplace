using HBA.Shared.Domain.Events;

namespace HBA.Media.Domain.Assets.Events;

/// <summary>LES ÉVÉNEMENTS DU MÉDIA (cahier des charges §16).</summary>
public sealed record MediaReadyDomainEvent(
    Guid MediaId, string OwnerType, Guid OwnerId, string MediaType, string ObjectKey) : DomainEvent;

/// <summary>Le traitement a échoué.</summary>
public sealed record MediaProcessingFailedDomainEvent(
    Guid MediaId, string OwnerType, Guid OwnerId, string Reason) : DomainEvent;

/// <summary>Supprimé LOGIQUEMENT.</summary>
public sealed record MediaDeletedDomainEvent(
    Guid MediaId, string OwnerType, Guid OwnerId, string MediaType) : DomainEvent;
