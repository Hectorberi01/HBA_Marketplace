using System.Diagnostics;
using System.Security.Claims;
using HBA.Shared.Application.Context;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Remplit le contexte propagé du §18 à partir des en-têtes entrants, et le rend
/// disponible à tout le traitement de la requête.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI LE `requestId` EST GÉNÉRÉ ICI QUAND LE CLIENT N'EN FOURNIT PAS.
///
/// Le §5 met `meta.requestId` dans TOUTES les réponses, succès comme erreur. Si le
/// champ n'était rempli que lorsque le client pense à envoyer l'en-tête, il serait
/// vide précisément dans le cas qui compte : un client tiers, mal configuré, qui
/// rencontre une erreur et ne peut rien citer pour qu'on la retrouve.
///
/// Le `correlationId` suit une règle différente : il est REPRIS s'il existe, jamais
/// régénéré. C'est ce qui distingue les deux — le requestId identifie UN appel, le
/// correlationId identifie UN FLUX. Un checkout qui déclenche un paiement puis une
/// livraison produit trois requestId et un seul correlationId. Régénérer le second
/// à chaque bond couperait la chaîne exactement là où on la suit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RequestContextMiddleware
{
    /// <summary>En-tête portant l'identifiant de requête, à l'aller comme au retour.</summary>
    public const string RequestIdHeader = "x-request-id";

    /// <summary>En-tête portant l'identifiant de flux métier.</summary>
    public const string CorrelationIdHeader = "x-correlation-id";

    /// <summary>En-tête d'idempotence du §5.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly RequestDelegate _next;
    private readonly string _serviceName;
    private readonly string _serviceCode;

    public RequestContextMiddleware(RequestDelegate next, string serviceName, string serviceCode)
    {
        _next = next;
        _serviceName = serviceName;
        _serviceCode = serviceCode;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var requestId = FirstHeader(httpContext, RequestIdHeader) ?? NewId("req");

        // LA CORRÉLATION N'EST PAS RECALCULÉE ICI.
        //
        // `ServiceCorrelationMiddleware` s'exécute avant, reprend le `X-Correlation-ID`
        // de la passerelle et le dépose dans `HttpContext.Items`. En relire l'en-tête
        // pour notre compte donnerait le même résultat aujourd'hui — et divergerait au
        // premier changement de l'un des deux : deux identifiants pour une même requête,
        // l'un dans les journaux, l'autre dans `meta.correlationId`, et le rapprochement
        // redevient impossible. Une seule source, et c'est celle qui existait déjà.
        var correlationId = httpContext.Items.TryGetValue(ServiceCorrelationMiddleware.HeaderName, out var carried)
                            && carried is string carriedId
                            && !string.IsNullOrWhiteSpace(carriedId)
            ? carriedId
            : FirstHeader(httpContext, CorrelationIdHeader) ?? requestId;

        var context = new HbaRequestContext
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            // Activity.Current est renseigné par l'instrumentation OpenTelemetry en amont.
            // Absent, on laisse null plutôt que d'inventer un identifiant : un traceId
            // fabriqué ici ne correspondrait à aucune trace et ferait chercher pour rien.
            TraceId = Activity.Current?.TraceId.ToString(),
            Actor = ReadActor(httpContext.User),
            IdempotencyKey = FirstHeader(httpContext, IdempotencyKeyHeader),
            Locale = ReadLocale(httpContext),
            ServiceName = _serviceName,
            ServiceCode = _serviceCode
        };

        // Renvoyé systématiquement : c'est ce que l'utilisateur pourra citer, et ce que
        // le client peut journaliser sans avoir à lire le corps de la réponse.
        httpContext.Response.Headers[RequestIdHeader] = requestId;
        httpContext.Response.Headers[CorrelationIdHeader] = correlationId;

        using (HbaRequestContext.BeginScope(context))
        {
            await _next(httpContext);
        }
    }

    private static string? FirstHeader(HttpContext httpContext, string name)
    {
        if (!httpContext.Request.Headers.TryGetValue(name, out var values))
        {
            return null;
        }

        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ReadLocale(HttpContext httpContext)
    {
        var header = FirstHeader(httpContext, "Accept-Language");

        if (string.IsNullOrWhiteSpace(header))
        {
            return "fr-BJ";
        }

        // Première langue de la liste, sans le facteur de qualité. Une négociation
        // complète n'apporterait rien ici : la locale ne sert qu'au rendu des
        // notifications, et le service de notification refait sa propre résolution
        // à partir des préférences utilisateur, qui priment sur l'en-tête.
        var first = header.Split(',')[0].Split(';')[0].Trim();
        return string.IsNullOrWhiteSpace(first) ? "fr-BJ" : first;
    }

    private static HbaActor? ReadActor(ClaimsPrincipal? user)
    {
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            return null;
        }

        var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue("sub")
                 ?? string.Empty;

        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        return new HbaActor
        {
            // Le type d'acteur du §19.1 (`CUSTOMER`, `SELLER`, `DRIVER`, `ADMIN`) se
            // déduit du rôle principal. Sans rôle, `USER` plutôt que `SYSTEM` : un
            // appel authentifié n'est jamais un appel système, et le confondre
            // fausserait l'audit des actions.
            Type = roles.Length > 0 ? roles[0].ToUpperInvariant() : "USER",
            Id = id,
            Roles = roles
        };
    }

    private static string NewId(string prefix)
        => $"{prefix}_{Guid.NewGuid():N}";
}

/// <summary>Enregistrement du middleware.</summary>
public static class RequestContextMiddlewareExtensions
{
    /// <summary>
    /// À placer TÔT dans le pipeline, mais APRÈS l'authentification : le contexte
    /// capture l'acteur depuis <c>HttpContext.User</c>, qui est vide tant que
    /// <c>UseAuthentication</c> n'est pas passé. Placé avant, tout se remplirait
    /// sauf l'acteur — et l'absence d'acteur ne lève aucune erreur, elle se voit
    /// seulement des semaines plus tard dans un journal d'audit vide.
    /// </summary>
    /// <param name="serviceCode">
    /// Préfixe des codes `*_SERVICE_NOT_FOUND` (§10). Omis, il est déduit de
    /// <paramref name="serviceName"/> — ce qui est correct pour douze services sur
    /// seize, et FAUX pour `cart-service` (`MARKETPLACE_CART`), `order-service`
    /// (`MARKETPLACE_ORDER`), `seller-service` (`MERCHANT`) et `wallet-service`
    /// (`WALLET_AND_SETTLEMENT`). Ces quatre-là doivent le passer explicitement.
    /// </param>
    public static IApplicationBuilder UseHbaRequestContext(
        this IApplicationBuilder app, string serviceName, string? serviceCode = null)
        => app.UseMiddleware<RequestContextMiddleware>(serviceName, serviceCode ?? DeriveCode(serviceName));

    private static string DeriveCode(string serviceName)
    {
        var trimmed = serviceName.Trim();

        if (trimmed.EndsWith("-service", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"-service".Length];
        }

        return trimmed.Replace('-', '_').ToUpperInvariant();
    }
}
