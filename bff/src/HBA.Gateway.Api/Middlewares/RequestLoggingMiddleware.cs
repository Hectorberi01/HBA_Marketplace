using System.Diagnostics;

namespace HBA.Gateway.Api.Middlewares;

/// <summary>
/// Journalise une ligne structurée par requête : méthode, chemin, statut, durée.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(stopwatch);

            // ═════════════════════════════════════════════════════════════════
            // CE QUI EST JOURNALISÉ EST UNE LISTE FERMÉE. NE PAS L'ÉLARGIR
            //    SANS SE DEMANDER CE QUE LE CHAMP PEUT CONTENIR.
            //
            // Absents délibérément :
            //   • `Request.Headers` — contient `Authorization`. Un jeton en clair
            //     dans Loki est un jeton valide pour quiconque lit Loki, pendant
            //     toute sa durée de vie.
            //   • `Request.QueryString` — les réinitialisations de mot de passe et
            //     les vérifications d'e-mail transportent leur secret en query.
            //     `/api/auth/reset?token=...` journalisé, c'est le compte pris.
            //   • le corps — mots de passe, codes OTP, coordonnées de paiement.
            //
            // `Path` est conservé : il peut porter un identifiant de ressource,
            // ce qui est nécessaire au diagnostic, mais jamais un secret dans les
            // conventions de routage de la plateforme.
            // ═════════════════════════════════════════════════════════════════
            _logger.LogInformation(
                "{Method} {Path} → {StatusCode} en {ElapsedMilliseconds} ms [CorrelationId={CorrelationId}]",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                (int)elapsed.TotalMilliseconds,
                context.Items[CorrelationIdMiddleware.HeaderName]?.ToString());
        }
    }
}
