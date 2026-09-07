using System.Diagnostics;
using HBA.Gateway.Application.Abstractions;

namespace HBA.Gateway.Api.Middlewares;

/// <summary>
/// Garantit qu'une requête porte un identifiant de corrélation, le rend
/// disponible au reste du pipeline et le renvoie au client.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>
    /// UN IDENTIFIANT FOURNI PAR LE CLIENT EST UNE DONNÉE NON FIABLE.
    ///
    /// Il est recopié tel quel dans les journaux, propagé à treize services et
    /// renvoyé dans un en-tête de réponse. Sans borne ni filtre, un client peut y
    /// glisser un saut de ligne et FABRIQUER de fausses lignes de journal —
    /// jusqu'à simuler des entrées d'audit crédibles — ou envoyer 100 Ko à
    /// recopier sur chaque appel sortant.
    ///
    /// D'où : longueur bornée, et jeu de caractères restreint à ce qu'un
    /// identifiant a besoin d'être.
    /// </summary>
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
        //
        // Les en-têtes de réponse deviennent immuables dès le premier octet
        // écrit. YARP diffuse la réponse du service au fil de l'eau : au retour du
        // pipeline, l'écriture aurait déjà commencé et l'affectation aurait levé
        // une exception — sur la route proxy uniquement, donc invisible en test
        // sur les seules routes BFF.
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

/// <summary>
/// Support de portée requête de l'identifiant de corrélation.
/// </summary>
/// <remarks>
/// Il existe parce que <see cref="ICorrelationContext"/> est consommé par des
/// services de portée requête construits AVANT que le middleware ne s'exécute.
/// Un simple <c>IHttpContextAccessor</c> ferait l'affaire, mais exposerait tout
/// le contexte HTTP à la couche Application — ce que l'interface évite justement.
/// </remarks>
public sealed class CorrelationContextHolder : ICorrelationContext
{
    /// <summary>
    /// Vide tant que le middleware n'a pas tourné — ce qui n'arrive que hors
    /// pipeline HTTP. Jamais nul, pour qu'aucun appelant n'ait à s'en soucier.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;
}
