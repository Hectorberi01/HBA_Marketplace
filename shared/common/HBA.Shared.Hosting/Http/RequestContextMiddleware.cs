using System.Diagnostics;
using System.Security.Claims;
using HBA.Shared.Application.Context;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Remplit le contexte propagé du §18 à partir des en-têtes entrants, et le rend
/// disponible à tout le traitement de la requête.
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
        var correlationId = httpContext.Items.TryGetValue(ServiceCorrelationMiddleware.HeaderName, out var carried)
                            && carried is string carriedId
                            && !string.IsNullOrWhiteSpace(carriedId)
            ? carriedId
            : FirstHeader(httpContext, CorrelationIdHeader) ?? requestId;

        var context = new HbaRequestContext
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            // Activity.Current est renseigné par l'instrumentation OpenTelemetry en
            // amont.
            TraceId = Activity.Current?.TraceId.ToString(),
            Actor = ReadActor(httpContext.User),
            IdempotencyKey = FirstHeader(httpContext, IdempotencyKeyHeader),
            Locale = ReadLocale(httpContext),
            ServiceName = _serviceName,
            ServiceCode = _serviceCode
        };

        // Renvoyé systématiquement : c'est ce que l'utilisateur pourra citer, et ce
        // que le client peut journaliser sans avoir à lire le corps de la réponse.
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

        // Première langue de la liste, sans le facteur de qualité.
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
            // Le type d'acteur du §19.1 (`CUSTOMER`, `SELLER`, `DRIVER`, `ADMIN`)
            // se déduit du rôle principal.
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
    /// capture l'acteur depuis <c> HttpContext.User</c>, qui est vide tant que <c>
    /// UseAuthentication</c> n'est pas passé.
    /// </summary>
    /// <param name="serviceCode">Préfixe des codes `*_SERVICE_NOT_FOUND` (§10).</param>
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
