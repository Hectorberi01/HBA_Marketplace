using HBA.Shared.Application.Context;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Traduit un Result métier en réponse HTTP au bord de l'API. Le mapping
/// Error.Type -> status code est centralisé ici : le métier ignore HTTP.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE FICHIER RENDAIT DU RFC 7807 ; IL REND MAINTENANT L'ENVELOPPE DU §5.
///
/// `Results.Problem(...)` produisait `{ "title": ..., "detail": ..., "status": ... }`.
/// Le cahier des charges impose `{ "success": false, "error": { "code", "message",
/// "details" }, "meta": { "requestId", "timestamp" } }`. Les deux formes n'ont aucun
/// champ en commun : ce n'est pas un enrichissement, c'est un remplacement.
///
/// Deux conséquences à assumer avant de livrer :
///
/// 1. TOUT endpoint passant par `Match` change de forme d'erreur. Les clients web
///    et mobile lisant `detail` ou `title` cassent. Ils doivent être livrés
///    ensemble, pas l'un après l'autre.
///
/// 2. Le succès aussi est enveloppé. `Match(x => Results.Ok(dto))` rendait `dto`
///    nu ; il faut désormais `Match(x => ApiResults.Ok(dto))`. Les appels existants
///    à `Results.Ok` continuent de compiler et de rendre l'ancienne forme — c'est
///    volontaire, pour que la migration se fasse endpoint par endpoint sans casser
///    la compilation d'un coup, mais cela veut dire qu'un endpoint non migré rend
///    encore l'ancienne forme en succès et la nouvelle en erreur. Cette incohérence
///    est TEMPORAIRE et doit être suivie : c'est le pire état des deux mondes.
/// ═════════════════════════════════════════════════════════════════════════════
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

    /// <summary>200 avec enveloppe de liste paginée : `meta.page`, `pageSize`, `total`, `hasNext`.</summary>
    public static IResult Page<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        long total,
        IReadOnlyDictionary<string, int>? facets = null)
        => Results.Json(
            ApiEnvelope.Page(items, page, pageSize, total, facets),
            statusCode: StatusCodes.Status200OK);

    /// <summary>
    /// 200 à partir d'un <see cref="PagedResult{T}"/>, facettes comprises.
    ///
    /// C'EST CETTE SURCHARGE QU'IL FAUT APPELER, PAS CELLE DU DESSUS.
    ///
    /// Écrire <c>Page(p.Items, p.Page, p.PageSize, p.Total)</c> compile, rend une
    /// réponse d'apparence correcte, et JETTE SILENCIEUSEMENT <c>p.Facets</c>. La
    /// console d'administration affiche alors un graphe de répartition vide, et
    /// l'on cherche la cause dans la requête ou dans la base — nulle part près de
    /// la ligne fautive.
    ///
    /// Prendre l'objet entier retire le choix : il n'y a plus de champ à oublier.
    /// </summary>
    public static IResult Page<T>(PagedResult<T> page)
        => Page(page.Items, page.Page, page.PageSize, page.Total, page.Facets);

    /// <summary>
    /// 404 enveloppé, avec le code <c>&lt;SERVICE&gt;_SERVICE_NOT_FOUND</c>.
    ///
    /// REMPLACE `Results.NotFound()`, QUI REND UN CORPS VIDE.
    ///
    /// Un 404 nu ne traverse pas l'enveloppe : le client reçoit une réponse sans
    /// `success`, sans `error.code` et surtout SANS `meta.requestId`. C'est
    /// précisément le cas où l'utilisateur envoie une capture d'écran et où l'on n'a
    /// rien à corréler avec les traces — la réponse dont on aurait le plus besoin de
    /// savoir d'où elle vient est la seule qui ne le dise pas.
    ///
    /// Le message reste volontairement vague. Les gardes d'appartenance rendent 404
    /// là où la ressource EXISTE mais n'appartient pas à l'appelant ; préciser
    /// laquelle des deux causes s'applique confirmerait l'existence à qui tâtonne.
    /// </summary>
    public static IResult NotFound(string serviceCode, string? message = null)
        => Failure(
            ErrorCodes.NotFound(serviceCode),
            message ?? "Ressource introuvable.",
            StatusCodes.Status404NotFound);

    /// <summary>
    /// 403 enveloppé pour une CAPACITÉ MANQUANTE — le refus opposé à un membre
    /// d'équipe dont le rôle ne porte pas la permission demandée.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI CE REFUS A SA PROPRE FABRIQUE, ET NE S'ÉCRIT PAS À LA MAIN.
    ///
    /// Il est rendu par une soixantaine de routes réparties dans cinq services.
    /// Écrit sur place, il y aurait soixante messages, soixante orthographes du
    /// champ `reason`, et une application mobile incapable de distinguer « votre
    /// rôle ne suffit pas » de « ce n'est pas votre dossier » — les deux sortant
    /// en `FORBIDDEN` avec un texte français différent à chaque route.
    ///
    /// LA CAPACITÉ MANQUANTE VOYAGE, ET CE N'EST PAS UNE FUITE.
    ///
    /// `details[reason]` porte `capability.missing:INVENTORY_ADJUST`. L'appelant
    /// apprend le NOM de la permission qu'il n'a pas — rien sur la ressource, rien
    /// sur qui la détient, rien sur l'existence de quoi que ce soit. Sans elle,
    /// l'application ne peut pas écrire l'écran qui compte : « demandez
    /// INVENTORY_ADJUST à votre gérant ». Le vendeur, lui, ne verrait qu'un refus
    /// muet et appellerait le support.
    ///
    /// LE MESSAGE NE NOMME PAS LA PERMISSION, LE DÉTAIL SI.
    ///
    /// `error.message` s'affiche tel quel dans les clients qui n'ont pas encore
    /// migré ; un code technique en majuscules y serait illisible pour un
    /// commerçant. Le code reste lisible par la machine, le texte par l'humain.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
    /// <remarks>
    /// CODE DISTINCT DE `MissingCapability`, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// Les deux sont des 403. Le premier est définitif — il faut un autre rôle ;
    /// le second se répare en dix secondes, en ressaisissant son mot de passe.
    /// Un client qui ne peut pas les distinguer affiche « demandez à votre
    /// gérant » là où il devrait ouvrir une boîte de dialogue.
    /// </remarks>
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

    /// <summary>
    /// Enveloppe d'erreur explicite, pour les cas hors <see cref="Result"/>.
    /// Nommée `Failure` et non `Error` : une méthode homonyme du type
    /// <see cref="Error"/> utilisé juste en dessous se lit mal, même si le
    /// compilateur sait les distinguer.
    /// </summary>
    public static IResult Failure(
        string code, string message, int statusCode, IReadOnlyList<ApiErrorDetail>? details = null)
        => Results.Json(ApiEnvelope.Fail(code, message, details), statusCode: statusCode);

    /// <summary>
    /// Status HTTP correspondant à un type d'erreur métier, selon le tableau du §5.
    /// Exposé publiquement parce que les intercepteurs gRPC et le filtre
    /// d'idempotence ont besoin du même mapping : deux tables divergeraient.
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

    /// <summary>
    /// Code normalisé du §10 correspondant à un type d'erreur métier.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI LE CODE VIENT DU TYPE ET NON DE `Error.Code`.
    ///
    /// Le domaine produit des codes fins et utiles : `users.profile.avatar_too_long`,
    /// `users.address.limit_reached`. Le cahier des charges, lui, n'en connaît que
    /// cinq — `VALIDATION_ERROR`, `BUSINESS_RULE_VIOLATION`, `CONFLICT`,
    /// `DEPENDENCY_UNAVAILABLE`, `&lt;SERVICE&gt;_SERVICE_NOT_FOUND`. Les deux ne sont
    /// pas concurrents : le premier dit CE QUI s'est passé, le second dit COMMENT le
    /// client doit réagir.
    ///
    /// Dériver le code normalisé du TYPE d'erreur rend conformes, d'un seul fichier,
    /// les endpoints des seize services — sans toucher un seul handler. L'audit du
    /// 17 août comptait 0/16 services portant ces codes ; ils y sont tous désormais.
    ///
    /// Le code fin n'est pas perdu pour autant : il part dans `details`, sous le
    /// champ `reason`. Le supprimer aurait été une régression réelle — une
    /// application mobile ne peut pas distinguer « avatar trop long » de « nom trop
    /// long » si les deux se réduisent à `VALIDATION_ERROR` accompagné d'un message
    /// en français.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    /// <summary>
    /// Préfixe du service, posé par <c>UseHbaRequestContext</c>. `UNKNOWN` si le
    /// middleware n'est pas branché : mieux vaut un `UNKNOWN_SERVICE_NOT_FOUND`
    /// visible et cherchable qu'un `_SERVICE_NOT_FOUND` amputé que personne ne
    /// rattachera à un middleware oublié.
    /// </summary>
    private static string ServiceCode()
    {
        var code = HbaRequestContext.Current.ServiceCode;
        return string.IsNullOrWhiteSpace(code) ? "UNKNOWN" : code!;
    }

    /// <summary>
    /// Reporte le code fin du domaine dans `details`, ou rien s'il est absent.
    /// Un `Error.None` rendu par erreur donnerait un code vide : on n'ajoute alors
    /// aucun détail plutôt qu'un détail vide, qui ferait chercher une cause
    /// inexistante.
    /// </summary>
    private static IReadOnlyList<ApiErrorDetail>? Reason(string? domainCode)
        => string.IsNullOrWhiteSpace(domainCode)
            ? null
            : [new ApiErrorDetail { Field = "reason", Message = domainCode! }];
}
