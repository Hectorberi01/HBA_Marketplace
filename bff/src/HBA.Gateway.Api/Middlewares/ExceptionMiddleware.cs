using System.Diagnostics;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Mvc;
using HBA.Gateway.Api.Extensions;

namespace HBA.Gateway.Api.Middlewares;

/// <summary>
/// Convertit toute exception non gérée en réponse <c>application/problem+json</c>
/// uniforme, sans rien divulguer de l'intérieur de la plateforme.
/// </summary>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
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
            // UN CLIENT QUI RACCROCHE N'EST PAS UNE ERREUR SERVEUR.
            //
            // Sur mobile — passage en tunnel, bascule Wi-Fi/4G, application mise
            // en arrière-plan — cela arrive en permanence. Compté en 500, ce cas
            // noie les vraies pannes dans les tableaux de bord et déclenche des
            // alertes sur un réseau béninois qui a simplement vacillé.
            //
            // 499 est la convention nginx pour « le client est parti ». Aucun
            // corps n'est écrit : il n'y a plus personne pour le lire.
            _logger.LogDebug(
                "Requête abandonnée par le client : {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX ÉCHECS D'AGRÉGATION SONT TRAITÉS AVANT LE CAS GÉNÉRAL.
        //
        // Sans ces deux clauses, une dépendance critique en panne rendrait 500 —
        // c'est-à-dire « la passerelle a un défaut » — alors que la passerelle a
        // parfaitement fonctionné et qu'un service amont est à terre. La
        // distinction n'est pas cosmétique : elle décide qui est réveillé la nuit.
        // ═════════════════════════════════════════════════════════════════════
        catch (BffResourceNotFoundException exception)
        {
            // 404 : la ressource n'existe pas. Aucun journal d'erreur — un produit
            // supprimé consulté depuis un favori n'est pas un incident.
            await WriteProblemAsync(
                context,
                StatusCodes.Status404NotFound,
                "not-found",
                "Not Found",
                "La ressource demandée est introuvable.",
                exception);
        }
        catch (CriticalDependencyException exception)
        {
            // LE NOM DU SERVICE VA DANS LE JOURNAL, PAS DANS LA RÉPONSE.
            //
            // « catalog-service injoignable » renseignerait un attaquant sur la
            // topologie interne et sur le composant à cibler ensuite.
            _logger.LogError(
                exception,
                "Dépendance critique indisponible : {Dependency} ({StatusCode}) sur {Path}",
                exception.Source, exception.StatusCode, context.Request.Path);

            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "dependency-unavailable",
                "Service Unavailable",
                "Un service nécessaire à cette requête est momentanément indisponible.",
                exception);
        }
        catch (Exception exception)
        {
            var correlationId = context.Items[CorrelationIdMiddleware.HeaderName]?.ToString();

            // L'EXCEPTION COMPLÈTE VA DANS LES JOURNAUX, JAMAIS DANS LA RÉPONSE.
            //
            // C'est toute la ligne de partage : le diagnostic est intégralement
            // conservé côté serveur, corrélé, et le client ne reçoit qu'un
            // identifiant à communiquer au support.
            _logger.LogError(
                exception,
                "Exception non gérée sur {Method} {Path}. [CorrelationId={CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            if (context.Response.HasStarted)
            {
                // La réponse est déjà partie — cas courant lorsque YARP diffuse le
                // flux d'un service qui se coupe en cours de route. On ne peut plus
                // rien réécrire ; relancer laisse le serveur couper proprement la
                // connexion plutôt que d'envoyer un corps tronqué mais « valide ».
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problem = new ProblemDetails
            {
                Type = "https://api.hba-express.com/errors/internal-server-error",
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,

                // Message FIXE. Y interpoler `exception.Message` ferait fuiter
                // selon l'exception : une chaîne de connexion PostgreSQL, un nom
                // d'hôte interne, un fragment de requête SQL, voire un jeton
                // présent dans l'URL d'un appel sortant.
                Detail = "Une erreur inattendue est survenue lors du traitement de la requête.",
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            problem.Extensions["correlationId"] = correlationId;

            // LE TYPE EST PASSÉ ICI : `WriteAsJsonAsync` écrase `ContentType`.
            //
            // Le poser sur la réponse avant l'appel ne survit pas — la surcharge
            // sans `contentType` réécrit « application/json; charset=utf-8 ».
            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: RateLimitingExtensions.ProblemJson,
                context.RequestAborted);
        }
    }

    /// <summary>
    /// Écrit un <c>application/problem+json</c> sans jamais exposer le motif
    /// interne de l'exception.
    /// </summary>
    /// <remarks>
    /// `detail` est un message FIXE choisi par l'appelant de cette méthode. Le
    /// paramètre <paramref name="exception"/> ne sert qu'à ne pas écrire de corps
    /// si la réponse est déjà partie — il n'est jamais sérialisé.
    /// </remarks>
    private static async Task WriteProblemAsync(
        HttpContext context, int statusCode, string slug, string title, string detail, Exception exception)
    {
        _ = exception;

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Type = $"https://api.hba-express.com/errors/{slug}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        problem.Extensions["correlationId"] =
            context.Items[CorrelationIdMiddleware.HeaderName]?.ToString();

        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: RateLimitingExtensions.ProblemJson,
            context.RequestAborted);
    }
}
