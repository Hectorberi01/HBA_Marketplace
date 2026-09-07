using System.Diagnostics;

namespace HBA.Gateway.Api.Middlewares;

/// <summary>Journalise une ligne structurée par requête : méthode, chemin, statut, durée.</summary>
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

            // CE QUI EST JOURNALISÉ EST UNE LISTE FERMÉE. NE PAS L'ÉLARGIR SANS SE
            // DEMANDER CE QUE LE CHAMP PEUT CONTENIR.
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
