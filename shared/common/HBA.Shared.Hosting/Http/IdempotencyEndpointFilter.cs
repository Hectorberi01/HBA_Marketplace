using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HBA.Shared.Application.Context;
using HBA.Shared.Domain.Results;
using HBA.Shared.Infrastructure.Idempotency;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Applique l'en-tête <c>Idempotency-Key</c> du §5 à un endpoint.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CE FILTRE GARANTIT, ET CE QU'IL NE GARANTIT PAS.
///
/// Il garantit qu'une même clé, pour un même utilisateur et un même endpoint, ne
/// produit qu'UNE exécution : les tentatives suivantes rejouent la réponse
/// mémorisée. C'est ce qui empêche un double débit quand le réseau tombe entre le
/// traitement et l'affichage.
///
/// Il ne garantit PAS l'atomicité entre l'effet métier et la mémorisation de la
/// réponse. `CompleteAsync` s'exécute APRÈS le handler, dans une écriture séparée.
/// Si le processus meurt entre les deux, la commande existe et la réponse n'est pas
/// mémorisée : le rejeu retrouve la clé en état « en cours » et rend 409 plutôt que
/// de créer un doublon. Le client voit un conflit là où il attendait sa commande —
/// c'est désagréable, mais c'est le bon compromis : mieux vaut un 409 à expliquer
/// qu'un second paiement à rembourser.
///
/// La seule façon d'obtenir l'atomicité serait d'écrire la réservation dans la
/// transaction métier elle-même, donc de faire remonter l'idempotence dans le
/// handler. C'est un choix à faire service par service pour les endpoints de
/// paiement ; ce filtre couvre le cas général.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class IdempotencyEndpointFilter : IEndpointFilter
{
    private readonly bool _required;

    /// <param name="required">
    /// Vrai pour les POST de création, de paiement et de checkout, où le §5 rend
    /// l'en-tête obligatoire. Faux ailleurs : la clé est alors honorée si elle est
    /// fournie, et l'absence laisse simplement passer.
    /// </param>
    public IdempotencyEndpointFilter(bool required) => _required = required;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var key = HbaRequestContext.Current.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (!_required)
            {
                return await next(context);
            }

            return ApiResults.Failure(
                ErrorCodes.ValidationError,
                "L'en-tête Idempotency-Key est obligatoire sur cette opération.",
                StatusCodes.Status400BadRequest,
                [new ApiErrorDetail { Field = RequestContextMiddleware.IdempotencyKeyHeader, Message = "Absent." }]);
        }

        var store = httpContext.RequestServices.GetService<IIdempotencyStore>();

        if (store is null)
        {
            // Aucun store enregistré : le service n'a pas encore sa table. On laisse
            // passer plutôt que d'échouer — un filtre mal câblé ne doit pas rendre
            // l'endpoint inutilisable — mais on le dit fort, sinon l'absence de
            // protection passerait inaperçue jusqu'au premier double paiement.
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<IdempotencyEndpointFilter>()
                .LogError(
                    "Idempotency-Key reçue sur {Endpoint} mais aucun IIdempotencyStore n'est enregistré : " +
                    "la requête s'exécute SANS protection contre le rejeu.",
                    EndpointKey(httpContext));

            return await next(context);
        }

        var scope = HbaRequestContext.Current.Actor?.Id ?? string.Empty;
        var endpoint = EndpointKey(httpContext);
        var fingerprint = await FingerprintAsync(httpContext);

        var reservation = await store.TryBeginAsync(key!, scope, endpoint, fingerprint, httpContext.RequestAborted);

        switch (reservation.Outcome)
        {
            case IdempotencyOutcome.Replay:
                return Replay(reservation);

            case IdempotencyOutcome.InFlight:
                return ApiResults.Failure(
                    ErrorCodes.Conflict,
                    "Une requête portant cette clé d'idempotence est encore en cours de traitement.",
                    StatusCodes.Status409Conflict);

            case IdempotencyOutcome.Mismatch:
                return ApiResults.Failure(
                    ErrorCodes.Conflict,
                    "Cette clé d'idempotence a déjà été utilisée avec un corps de requête différent.",
                    StatusCodes.Status409Conflict);
        }

        object? result;

        try
        {
            result = await next(context);
        }
        catch
        {
            // Le handler a échoué : la clé doit redevenir utilisable, sinon le client
            // resterait bloqué 24 h sur une panne passagère.
            await store.AbandonAsync(key!, scope, endpoint, CancellationToken.None);
            throw;
        }

        var (statusCode, body) = Describe(result);
        await store.CompleteAsync(key!, scope, endpoint, statusCode, body, CancellationToken.None);

        return result;
    }

    private static IResult Replay(IdempotencyReservation reservation)
        => reservation.ResponseBody is null
            ? Results.StatusCode(reservation.StatusCode)
            : Results.Text(reservation.ResponseBody, "application/json", Encoding.UTF8, reservation.StatusCode);

    private static string EndpointKey(HttpContext httpContext)
        => $"{httpContext.Request.Method} {httpContext.Request.Path}";

    /// <summary>
    /// Empreinte du corps de la requête. `EnableBuffering` est indispensable : sans
    /// lui, lire le flux ici le consommerait et le handler recevrait un corps vide.
    /// </summary>
    private static async Task<string> FingerprintAsync(HttpContext httpContext)
    {
        httpContext.Request.EnableBuffering();
        httpContext.Request.Body.Position = 0;

        using var memory = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(memory);
        httpContext.Request.Body.Position = 0;

        return Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant();
    }

    /// <summary>
    /// Extrait status et corps du résultat pour mémorisation. Un résultat dont on ne
    /// sait pas lire la valeur est mémorisé avec un corps null : la tentative suivante
    /// rejouera alors le status seul. Mieux vaut un rejeu partiel qu'une réexécution.
    /// </summary>
    private static (int StatusCode, string? Body) Describe(object? result)
    {
        var statusCode = result is IStatusCodeHttpResult { StatusCode: not null } coded
            ? coded.StatusCode!.Value
            : StatusCodes.Status200OK;

        if (result is IValueHttpResult { Value: not null } valued)
        {
            return (statusCode, JsonSerializer.Serialize(valued.Value));
        }

        return (statusCode, null);
    }
}

/// <summary>Raccourcis d'attachement du filtre à un endpoint ou à un groupe.</summary>
public static class IdempotencyEndpointFilterExtensions
{
    /// <summary>Idempotence obligatoire : création, paiement, checkout (§5).</summary>
    public static TBuilder RequireIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(new IdempotencyEndpointFilter(required: true));

    /// <summary>Idempotence honorée si la clé est fournie, sans l'exiger.</summary>
    public static TBuilder AllowIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(new IdempotencyEndpointFilter(required: false));
}
