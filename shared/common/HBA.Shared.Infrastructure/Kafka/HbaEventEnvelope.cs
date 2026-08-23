using System.Text.Json;
using System.Text.Json.Serialization;

namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>
/// Enveloppe canonique du §19.1 du cahier des charges.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UNE SECONDE ENVELOPPE À CÔTÉ DE `KafkaEventEnvelope`.
///
/// `KafkaEventEnvelope` existe déjà et transporte à peu près les mêmes idées, mais
/// pas le même contrat : il n'a ni `environment`, ni `actor`, ni `partitionKey`, ni
/// `metadata.schema`, il expose un `SequenceNumber` que la spec ne connaît pas, et
/// son `aggregate` est aplati en deux champs au lieu d'un objet.
///
/// Les remplacer d'un coup romprait tous les consumers en vol. Les deux cohabitent
/// donc le temps de la migration : les événements portant `[HbaEvent]` sont publiés
/// dans CETTE enveloppe, les autres restent sur l'ancienne. La bascule se fait
/// événement par événement, et l'attribut est la preuve qu'elle a été faite.
///
/// Les noms JSON sont figés en camelCase par attribut, sans dépendre d'une
/// convention de sérialisation configurée ailleurs : ce contrat est lu par des
/// services qui ne sont pas tous en .NET.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record HbaEventEnvelope
{
    /// <summary>UUID v7 du message. Clé d'idempotence côté consumer (§19.5).</summary>
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    /// <summary>Nom métier stable `&lt;domaine&gt;.&lt;agrégat&gt;.&lt;action passée&gt;`.</summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    /// <summary>Version entière du contrat de l'événement.</summary>
    [JsonPropertyName("eventVersion")]
    public int EventVersion { get; init; } = 1;

    /// <summary>Date UTC RFC3339 de production de l'événement métier.</summary>
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Nom du microservice producteur, ex. `food-order-service`.</summary>
    [JsonPropertyName("producer")]
    public string Producer { get; init; } = string.Empty;

    /// <summary>`local`, `staging` ou `production`.</summary>
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = "local";

    /// <summary>Identifiant commun à tout un flux métier distribué.</summary>
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Commande ou événement ayant causé celui-ci. Recommandé, pas obligatoire.</summary>
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

    /// <summary>
    /// Clé utilisée pour préserver l'ordre d'un agrégat (§19.2). Vaut l'identifiant
    /// de l'agrégat principal : tous les événements d'une même commande partent ainsi
    /// dans la même partition et conservent leur ordre.
    /// </summary>
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

    /// <summary>
    /// Version de l'agrégat après l'événement. Permet à un consumer de détecter
    /// un message arrivé dans le désordre après un rejeu, ce que l'horodatage seul
    /// ne permet pas quand deux écritures tombent dans la même milliseconde.
    /// </summary>
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

/// <summary>Métadonnées techniques (§19.1 `metadata`).</summary>
public sealed record HbaEventMetadata
{
    /// <summary>Nom de schéma versionné, ex. `hba.food.order.accepted.v1`.</summary>
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "application/json";

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "fr-BJ";
}
