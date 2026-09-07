using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Hosting;

/// <summary>Reprend l'identifiant de corrélation posé par la passerelle.</summary>
public sealed class ServiceCorrelationMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public ServiceCorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // ON REPREND CELUI DE LA PASSERELLE, ON N'EN REFABRIQUE PAS.
        var correlationId = context.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n");
        }

        context.Items[HeaderName] = correlationId;

        context.Response.OnStarting(state =>
        {
            ((HttpContext)state).Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        }, context);

        Activity.Current?.SetTag("hba.correlation_id", correlationId);

        await _next(context);
    }
}

/// <summary>Erreur uniforme en <c>application/problem+json</c>, sans fuite.</summary>
public sealed class ServiceExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ServiceExceptionMiddleware> _logger;

    public ServiceExceptionMiddleware(RequestDelegate next, ILogger<ServiceExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Le client — ici, souvent la passerelle — a raccroché.
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (DbUpdateConcurrencyException exception) when (!context.Response.HasStarted)
        {
            // `ConcurrencyExceptionHandler` N'A JAMAIS EXISTÉ — LOT 5.1.
            var correlationConcurrence = context.Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

            _logger.LogWarning(
                exception,
                "Conflit de concurrence optimiste sur {Method} {Path}. "
                + "C'est une garde qui a fonctionné, pas une panne : une autre transaction "
                + "a modifié la ligne entre la lecture et l'écriture. [CorrelationId={CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationConcurrence);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var concurrence = new ProblemDetails
            {
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,

                // Le message dit quoi FAIRE. « Erreur de concurrence » ne se
                // traduit en aucun geste pour la personne qui le lit sur son
                // téléphone ; « recommencez » si.
                Detail = "Cette ressource vient d'être modifiée par quelqu'un d'autre. "
                    + "Rechargez et recommencez.",
                Instance = context.Request.Path
            };

            concurrence.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            concurrence.Extensions["correlationId"] = correlationConcurrence;

            await context.Response.WriteAsJsonAsync(concurrence, context.RequestAborted);
        }
        catch (DbUpdateException exception) when (Doublon(exception) is { } contrainte && !context.Response.HasStarted)
        {
            // UNE CONTRAINTE D'UNICITÉ QUI MORD N'EST PAS UNE PANNE DU SERVICE.
            var correlationId = context.Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

            _logger.LogWarning(
                exception,
                "Doublon refusé par la contrainte {Contrainte} sur {Method} {Path}. "
                + "C'est une garde qui a fonctionné, pas une panne. [CorrelationId={CorrelationId}]",
                contrainte, context.Request.Method, context.Request.Path, correlationId);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var conflit = new ProblemDetails
            {
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = "Cette opération a déjà été enregistrée.",
                Instance = context.Request.Path
            };

            conflit.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            conflit.Extensions["correlationId"] = correlationId;

            await context.Response.WriteAsJsonAsync(conflit, context.RequestAborted);
        }
        catch (Exception exception)
        {
            var correlationId = context.Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

            _logger.LogError(
                exception,
                "Exception non gérée sur {Method} {Path}. [CorrelationId={CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,

                // MESSAGE FIXE. NE JAMAIS Y INTERPOLER `exception.Message`.
                Detail = "Une erreur inattendue est survenue lors du traitement de la requête.",
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            problem.Extensions["correlationId"] = correlationId;

            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
        }
    }

    /// <summary>
    /// Le nom de la contrainte d'unicité violée, ou <c> null</c> si ce n'en est pas
    /// une.
    /// </summary>
    private static string? Doublon(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: "23505" } postgres
            ? postgres.ConstraintName ?? "(sans nom)"
            : null;
}
