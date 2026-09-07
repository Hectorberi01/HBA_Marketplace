using System.Text.Json;
using System.Text.Json.Serialization;

namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>Enveloppe canonique du §19.1 du cahier des charges.</summary>
public sealed record HbaEventEnvelope
{
    /// <summary>UUID v7 du message. Clé d'idempotence côté consumer (§19.5).</summary>
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    /// <summary>
    /// Nom métier stable `&lt;domaine&gt;.&lt;agrégat&gt;.&lt;action passée&gt;`.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    /// <summary>Version entière du contrat de l'événement.</summary>
    [JsonPropertyName("eventVersion")]
    public int EventVersion { get; init; } = 1;

    /// <summary>Date UTC RFC3339 de production de l'événement métier.</summary>
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Nom du microservice producteur, ex.</summary>
    [JsonPropertyName("producer")]
    public string Producer { get; init; } = string.Empty;

    /// <summary>`local`, `staging` ou `production`.</summary>
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = "local";

    /// <summary>Identifiant commun à tout un flux métier distribué.</summary>
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Commande ou événement ayant causé celui-ci.</summary>
    [JsonPropertyName("causationId")]
    public string? CausationId { get; init; }

    /// <summary>Trace OpenTelemetry propagée entre gRPC et Kafka.</summary>
    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;

    /// <summary>Type, identifiant et version de l'agrégat métier concerné.</summary>
    [JsonPropertyName("aggregate")]
    public HbaEventAggregate Aggregate { get; init; } = new();

    /// <summary>Utilisateur ou service à l'origine de l'action.</summary>
    [JsonPropertyName("actor")]
    public HbaEventActor? Actor { get; init; }

    /// <summary>Périmètre logique de la donnée.</summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; init; } = "hba-bj";

    /// <summary>Clé utilisée pour préserver l'ordre d'un agrégat (§19.2).</summary>
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; init; } = string.Empty;

    /// <summary>Charge utile métier, spécifique au couple eventType/eventVersion.</summary>
    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }

    /// <summary>Schéma, type de contenu, locale et extensions techniques.</summary>
    [JsonPropertyName("metadata")]
    public HbaEventMetadata Metadata { get; init; } = new();
}

/// <summary>Agrégat concerné par l'événement (§19.1 `aggregate`).</summary>
public sealed record HbaEventAggregate
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Version de l'agrégat après l'événement.</summary>
    [JsonPropertyName("version")]
    public long Version { get; init; }
}

/// <summary>Acteur à l'origine de l'événement (§19.1 `actor`).</summary>
public sealed record HbaEventActor
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "SYSTEM";

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}

/// <summary>Métadonnées techniques (19.1 `metadata`).</summary>
public sealed record HbaEventMetadata
{
    /// <summary>Nom de schéma versionné, ex.</summary>
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "application/json";

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "fr-BJ";
}
