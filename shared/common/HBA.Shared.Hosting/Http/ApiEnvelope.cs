using System.Text.Json.Serialization;
using HBA.Shared.Application.Context;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Enveloppe de réponse externe du §5 du cahier des charges.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UNE ENVELOPPE PLUTÔT QUE LA RESSOURCE NUE, ET POURQUOI PAS RFC 7807.
///
/// Le bord HTTP rendait jusqu'ici la ressource directement en succès et un
/// `ProblemDetails` (RFC 7807) en erreur. Deux formes différentes selon l'issue :
/// le client doit tester le status code avant de savoir comment lire le corps, et
/// il n'a aucun endroit stable où trouver le `requestId` à citer dans un ticket.
///
/// Le §5 impose une forme unique — `success`, puis `data` OU `error`, plus `meta`.
/// Le client lit toujours la même structure, et le `requestId` est toujours au même
/// endroit, en succès comme en échec. C'est ce qui rend un incident racontable :
/// l'utilisateur envoie une capture, le `requestId` mène directement à la trace.
///
/// CHANGEMENT DE CONTRAT. Tout endpoint passant par <see cref="ApiResults"/>
/// change de forme de réponse. C'est l'objet même de la mise en conformité, mais
/// les clients web et mobile doivent être livrés avec — pas après.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ApiEnvelope<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Charge utile en cas de succès. Absente en cas d'erreur.</summary>
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
    /// <summary>Code stable, pris dans <c>ErrorCodes</c>. C'est lui que le client branche.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Message lisible. Destiné au diagnostic, pas à l'affichage tel quel.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Détails champ par champ pour les erreurs de validation. Toujours présent,
    /// éventuellement vide : un client qui itère dessus ne doit pas avoir à tester
    /// la nullité à chaque appel.
    /// </summary>
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
/// Bloc `meta` du §5, enrichi de la pagination du §10.4 quand la réponse est une liste.
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

    /// <summary>
    /// Répartition calculée sur l'ENSEMBLE filtré, pas sur la page servie.
    ///
    /// AJOUTÉ POUR NE PAS PERDRE UNE DONNÉE EN PASSANT À L'ENVELOPPE.
    ///
    /// `PagedResult` porte des facettes depuis toujours — la répartition du
    /// catalogue par statut, qu'affiche la console d'administration. La première
    /// version de `ApiResults.Page` ne prenait que (items, page, pageSize, total) :
    /// enveloppée par elle, la réponse aurait perdu les facettes SANS RIEN CASSER
    /// à la compilation. Le graphe de la console serait simplement devenu vide, et
    /// l'on aurait cherché la cause dans la requête.
    ///
    /// Nullable et omis à la sérialisation : les listes sans facettes rendent
    /// exactement ce qu'elles rendaient.
    /// </summary>
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
