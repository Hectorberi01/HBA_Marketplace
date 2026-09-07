using System.Text.Json.Serialization;
using HBA.Shared.Application.Context;

namespace HBA.Shared.Hosting.Http;

/// <summary>Enveloppe de réponse externe du §5 du cahier des charges.</summary>
public sealed record ApiEnvelope<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Charge utile en cas de succès.</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; init; }

    /// <summary>Détail de l'erreur. Absent en cas de succès.</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiError? Error { get; init; }

    [JsonPropertyName("meta")]
    public ApiMeta Meta { get; init; } = new();
}

/// <summary>Bloc `error` du §5.</summary>
public sealed record ApiError
{
    /// <summary>Code stable, pris dans <c>ErrorCodes</c>.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Message lisible. Destiné au diagnostic, pas à l'affichage tel quel.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>Détails champ par champ pour les erreurs de validation.</summary>
    [JsonPropertyName("details")]
    public IReadOnlyList<ApiErrorDetail> Details { get; init; } = Array.Empty<ApiErrorDetail>();
}

/// <summary>Erreur de validation localisée sur un champ.</summary>
public sealed record ApiErrorDetail
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Bloc `meta` du §5, enrichi de la pagination du §10.4 quand la réponse est une
/// liste.
/// </summary>
public sealed record ApiMeta
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("page")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Page { get; init; }

    [JsonPropertyName("pageSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PageSize { get; init; }

    [JsonPropertyName("total")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Total { get; init; }

    [JsonPropertyName("hasNext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasNext { get; init; }

    /// <summary>Répartition calculée sur l'ENSEMBLE filtré, pas sur la page servie.</summary>
    [JsonPropertyName("facets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, int>? Facets { get; init; }
}

/// <summary>Fabriques d'enveloppes. Le `requestId` est repris du contexte propagé (§18).</summary>
public static class ApiEnvelope
{
    public static ApiEnvelope<T> Ok<T>(T data, ApiMeta? meta = null)
        => new() { Success = true, Data = data, Meta = meta ?? Meta() };

    public static ApiEnvelope<IReadOnlyList<T>> Page<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        long total,
        IReadOnlyDictionary<string, int>? facets = null)
        => new()
        {
            Success = true,
            Data = items,
            Meta = Meta() with
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                HasNext = (long)page * pageSize < total,
                Facets = facets
            }
        };

    public static ApiEnvelope<object> Fail(
        string code, string message, IReadOnlyList<ApiErrorDetail>? details = null)
        => new()
        {
            Success = false,
            Error = new ApiError
            {
                Code = code,
                Message = message,
                Details = details ?? Array.Empty<ApiErrorDetail>()
            },
            Meta = Meta()
        };

    /// <summary>Métadonnées courantes : requestId du contexte propagé, horodatage UTC.</summary>
    public static ApiMeta Meta() => new()
    {
        RequestId = HbaRequestContext.Current.RequestId,
        Timestamp = DateTimeOffset.UtcNow
    };
}
