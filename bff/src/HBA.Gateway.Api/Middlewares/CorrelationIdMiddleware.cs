using System.Diagnostics;
using HBA.Gateway.Application.Abstractions;

namespace HBA.Gateway.Api.Middlewares;

/// <summary>
/// Garantit qu'une requête porte un identifiant de corrélation, le rend disponible
/// au reste du pipeline et le renvoie au client.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>UN IDENTIFIANT FOURNI PAR LE CLIENT EST UNE DONNÉE NON FIABLE.</summary>
    private const int MaxLength = 128;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, CorrelationContextHolder holder)
    {
        var correlationId = Accept(context.Request.Headers[HeaderName].ToString())
            ?? Activity.Current?.TraceId.ToString()
            ?? Guid.NewGuid().ToString("n");

        holder.CorrelationId = correlationId;
        context.Items[HeaderName] = correlationId;

        // Propagé aux services par la liste blanche de sortie : on réécrit
        // l'en-tête ENTRANT pour que YARP transmette la valeur validée, et non
        // celle d'origine — sans quoi le filtrage ci-dessus serait décoratif.
        context.Request.Headers[HeaderName] = correlationId;

        // `OnStarting` ET NON UNE ÉCRITURE DIRECTE.
        context.Response.OnStarting(state =>
        {
            var response = ((HttpContext)state).Response;
            response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        }, context);

        // Rattache l'identifiant à la trace OpenTelemetry en cours : c'est ce qui
        // permet de passer d'une ligne de journal à la trace distribuée complète.
        Activity.Current?.SetTag("hba.correlation_id", correlationId);

        await _next(context);
    }

    private static string? Accept(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return null;
        }

        foreach (var character in candidate)
        {
            var acceptable = char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':';

            if (!acceptable)
            {
                return null;
            }
        }

        return candidate;
    }
}

/// <summary>Support de portée requête de l'identifiant de corrélation.</summary>
public sealed class CorrelationContextHolder : ICorrelationContext
{
    /// <summary>
    /// Vide tant que le middleware n'a pas tourné — ce qui n'arrive que hors
    /// pipeline HTTP. Jamais nul, pour qu'aucun appelant n'ait à s'en soucier.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;
}
