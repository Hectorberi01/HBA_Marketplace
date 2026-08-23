using System.Text.Json;

namespace HBA.Shared.Infrastructure.Kafka;

public sealed record KafkaEventEnvelope(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset PublishedAt,
    string Producer,
    string ProducerVersion,
    string CorrelationId,
    string? CausationId,
    string? SagaId,
    string AggregateType,
    string AggregateId,
    long SequenceNumber,
    string? TenantId,
    JsonElement Data,
    IReadOnlyDictionary<string, object?> Metadata);
