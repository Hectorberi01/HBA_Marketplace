using HBA.Shared.Infrastructure.Persistence;
using HBA.Media.Infrastructure.Persistence;
using HBA.Media.Infrastructure.Persistence.Outbox;
using HBA.Media.Infrastructure.Messaging.Kafka.Retry;
using HBA.Media.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Inbox`.

namespace HBA.Media.Infrastructure.Persistence.Inbox;

/// <summary>Trace d'un événement Kafka déjà traité par un consumer donné (§19.5).</summary>
public sealed class ConsumerInboxEntry : IEntreeDInbox
{
    /// <summary>`eventId` de l'enveloppe Kafka (§19.1).</summary>
    public Guid EventId { get; init; }

    /// <summary>Nom du consumer / handler, ex.</summary>
    public string ConsumerName { get; init; } = default!;

    /// <summary>Type d'événement, conservé pour le diagnostic et les rejeux ciblés.</summary>
    public string EventType { get; init; } = default!;

    /// <summary>Date du traitement réussi.</summary>
    public DateTime ProcessedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Corrélation distribuée, pour relier cette consommation au flux d'origine.</summary>
    public string? CorrelationId { get; init; }
}
