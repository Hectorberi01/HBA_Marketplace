using HBA.Shared.Application.Context;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Traduit un Result métier en réponse HTTP au bord de l'API. Le mapping Error.Type
/// -> status code est centralisé ici : le métier ignore HTTP.
/// </summary>
public static class ApiResults
{
    public static IResult Match<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess)
        => result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error);

    public static IResult Match(this Result result, Func<IResult> onSuccess)
        => result.IsSuccess ? onSuccess() : Problem(result.Error);

    /// <summary>200 avec enveloppe de succès.</summary>
    public static IResult Ok<T>(T data)
        => Results.Json(ApiEnvelope.Ok(data), statusCode: StatusCodes.Status200OK);

    /// <summary>201 avec enveloppe de succès et en-tête <c>Location</c>.</summary>
    public static IResult Created<T>(T data, string? location = null)
    {
        var envelope = ApiEnvelope.Ok(data);

        return location is null
            ? Results.Json(envelope, statusCode: StatusCodes.Status201Created)
            : Results.Created(location, envelope);
    }

    /// <summary>202 pour un traitement asynchrone accepté (remboursements, payouts).</summary>
    public static IResult Accepted<T>(T data)
        => Results.Json(ApiEnvelope.Ok(data), statusCode: StatusCodes.Status202Accepted);

    /// <summary>
    /// 200 avec enveloppe de liste paginée : `meta.page`, `pageSize`, `total`,
    /// `hasNext`.
    /// </summary>
    public static IResult Page<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        long total,
        IReadOnlyDictionary<string, int>? facets = null)
        => Results.Json(
            ApiEnvelope.Page(items, page, pageSize, total, facets),
            statusCode: StatusCodes.Status200OK);

    /// <summary>200 à partir d'un <see cref="PagedResult{T}"/>, facettes comprises.</summary>
    public static IResult Page<T>(PagedResult<T> page)
        => Page(page.Items, page.Page, page.PageSize, page.Total, page.Facets);

    /// <summary>404 enveloppé, avec le code <c>&lt;SERVICE&gt;_SERVICE_NOT_FOUND</c>.</summary>
    public static IResult NotFound(string serviceCode, string? message = null)
        => Failure(
            ErrorCodes.NotFound(serviceCode),
            message ?? "Ressource introuvable.",
            StatusCodes.Status404NotFound);

    /// <summary>
    /// 403 enveloppé pour une CAPACITÉ MANQUANTE — le refus opposé à un membre
    /// d'équipe dont le rôle ne porte pas la permission demandée.
    /// </summary>
    public static IResult MissingCapability(string capability, string? message = null)
        => Failure(
            ErrorCodes.Forbidden,
            message ?? "Votre rôle ne vous autorise pas cette action.",
            StatusCodes.Status403Forbidden,
            [new ApiErrorDetail { Field = "reason", Message = $"capability.missing:{capability}" }]);

    /// <summary>
    /// 403 enveloppé pour une STEP-UP MANQUANTE — l'appelant a la permission, mais
    /// son authentification est trop ancienne pour un geste Critique (§H).
    /// </summary>
    public static IResult ReauthenticationRequired(string capability)
        => Failure(
            ErrorCodes.Forbidden,
            "Cette action exige une confirmation récente de votre mot de passe.",
            StatusCodes.Status403Forbidden,
            [new ApiErrorDetail { Field = "reason", Message = $"reauthentication.required:{capability}" }]);

    /// <summary>401 enveloppé. Même raison que <see cref="NotFound"/> : le requestId.</summary>
    public static IResult Unauthorized(string? message = null)
        => Failure(
            ErrorCodes.Unauthorized,
            message ?? "Authentification requise.",
            StatusCodes.Status401Unauthorized);

    /// <summary>Enveloppe d'erreur explicite, pour les cas hors <see cref="Result"/>.</summary>
    public static IResult Failure(
        string code, string message, int statusCode, IReadOnlyList<ApiErrorDetail>? details = null)
        => Results.Json(ApiEnvelope.Fail(code, message, details), statusCode: statusCode);

    /// <summary>
    /// Status HTTP correspondant à un type d'erreur métier, selon le tableau du §5.
    /// </summary>
    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.DependencyUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    private static IResult Problem(Error error)
    {
        return Results.Json(
            ApiEnvelope.Fail(Normalize(error.Type), error.Message, Reason(error.Code)),
            statusCode: StatusFor(error.Type));
    }

    /// <summary>Code normalisé du §10 correspondant à un type d'erreur métier.</summary>
    private static string Normalize(ErrorType type) => type switch
    {
        ErrorType.Validation => ErrorCodes.ValidationError,
        ErrorType.Unauthorized => ErrorCodes.Unauthorized,
        ErrorType.Forbidden => ErrorCodes.Forbidden,
        ErrorType.NotFound => ErrorCodes.NotFound(ServiceCode()),
        ErrorType.Conflict => ErrorCodes.Conflict,
        ErrorType.BusinessRule => ErrorCodes.BusinessRuleViolation,
        ErrorType.DependencyUnavailable => ErrorCodes.DependencyUnavailable,
        _ => ErrorCodes.InternalError
    };

    /// <summary>Préfixe du service, posé par <c>UseHbaRequestContext</c>.</summary>
    private static string ServiceCode()
    {
        var code = HbaRequestContext.Current.ServiceCode;
        return string.IsNullOrWhiteSpace(code) ? "UNKNOWN" : code!;
    }

    /// <summary>Reporte le code fin du domaine dans `details`, ou rien s'il est absent.</summary>
    private static IReadOnlyList<ApiErrorDetail>? Reason(string? domainCode)
        => string.IsNullOrWhiteSpace(domainCode)
            ? null
            : [new ApiErrorDetail { Field = "reason", Message = domainCode! }];
}
