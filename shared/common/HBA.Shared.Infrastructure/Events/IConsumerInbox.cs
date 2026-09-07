// LE PORT RESTE, L'IMPLEMENTATION DESCEND.

namespace HBA.Shared.Infrastructure.Events;

/// <summary>Garde d'idempotence côté consumer (§19.5).</summary>
public interface IConsumerInbox
{
    /// <summary>Vrai si ce couple (événement, consumer) a déjà été traité avec succès.</summary>
    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken = default);

    /// <summary>Enregistre la trace de traitement DANS la transaction métier en cours.</summary>
    Task MarkProcessedAsync(
        Guid eventId,
        string consumerName,
        string eventType,
        string? correlationId,
        CancellationToken cancellationToken = default);
}
