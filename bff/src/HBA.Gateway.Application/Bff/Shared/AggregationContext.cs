using System.Diagnostics;
using HBA.Gateway.Application.Abstractions.Services;

namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>
/// Accompagne une agrégation : trace, mesure, et applique la criticité.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE GABARIT QUE TOUS LES BFF REPRENNENT.
///
///     var ctx = AggregationContext.Start("client.express.home");
///
///     var categoriesTask = ctx.CallAsync("Catalog", () => catalog.ListCategoriesAsync(ct));
///     var recoTask       = ctx.CallAsync("Engagement", () => engagement.GetMyRecommendedProductIdsAsync(ct));
///     await Task.WhenAll(categoriesTask, recoTask);          // §22
///
///     var categories = ctx.Resolve(DependencyCriticality.Critical, "Catalog", await categoriesTask);
///     var reco       = ctx.Resolve(DependencyCriticality.Optional, "Engagement", await recoTask);
///
///     return ctx.Complete(dto);
///
/// `CallAsync` ET `Resolve` SONT SÉPARÉS, ET C'EST TOUTE L'ASTUCE.
///
/// Fondre les deux — une méthode qui appelle ET classe — forcerait un `await` par
/// dépendance, donc une exécution SÉQUENTIELLE. C'est exactement l'anti-modèle du
/// §22. En les séparant, le lancement est non bloquant et la classification n'a
/// lieu qu'après `Task.WhenAll`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class AggregationContext : IDisposable
{
    private readonly List<BffWarning> _warnings = [];
    private readonly Activity? _activity;
    private readonly long _startedAt;

    private AggregationContext(string screen)
    {
        Screen = screen;
        _activity = BffTelemetry.Source.StartActivity(screen, ActivityKind.Internal);
        _startedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>Identifiant d'écran, tel qu'il apparaît dans les traces.</summary>
    public string Screen { get; }

    public static AggregationContext Start(string screen) => new(screen);

    /// <summary>
    /// Lance un appel de dépendance en le traçant et en le mesurant.
    /// N'attend PAS : le résultat se récupère plus tard, après <c>Task.WhenAll</c>.
    /// </summary>
    public async Task<ServiceResult<T>> CallAsync<T>(
        string source, Func<Task<ServiceResult<T>>> call)
    {
        using var span = BffTelemetry.Source.StartActivity(
            $"{source.ToLowerInvariant()}.call", ActivityKind.Client);

        var startedAt = Stopwatch.GetTimestamp();

        var result = await call();

        var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        BffTelemetry.DependencyDuration.Record(
            elapsed,
            new KeyValuePair<string, object?>("bff.screen", Screen),
            new KeyValuePair<string, object?>("bff.dependency", source));

        span?.SetTag("bff.dependency", source);
        span?.SetTag("bff.status_code", result.StatusCode);

        if (!result.IsSuccess)
        {
            BffTelemetry.DependencyFailures.Add(
                1,
                new KeyValuePair<string, object?>("bff.screen", Screen),
                new KeyValuePair<string, object?>("bff.dependency", source));

            // LE MOTIF VA DANS LA TRACE, PAS DANS LA RÉPONSE.
            //
            // Il peut nommer un hôte interne. La trace est un canal d'exploitation ;
            // la réponse est publique.
            span?.SetStatus(ActivityStatusCode.Error, result.FailureReason);
        }

        return result;
    }

    /// <summary>
    /// Applique la criticité à un résultat déjà obtenu.
    /// </summary>
    /// <remarks>
    /// UN 401 SUR UNE DÉPENDANCE OPTIONNELLE N'EST PAS UNE DÉGRADATION.
    ///
    /// engagement-service est entièrement authentifié : un visiteur anonyme
    /// reçoit 401 sur la note d'un produit. Compter cela comme un incident
    /// remplirait le compteur d'échecs à chaque visite non connectée, et le
    /// signal utile — le service est vraiment tombé — serait noyé.
    /// </remarks>
    public T? Resolve<T>(DependencyCriticality criticality, string source, ServiceResult<T> result)
    {
        if (result.IsSuccess)
        {
            return result.Value;
        }

        if (result.IsNotFound && criticality == DependencyCriticality.Critical)
        {
            throw new BffResourceNotFoundException(source, "ressource amont");
        }

        switch (criticality)
        {
            case DependencyCriticality.Critical:
                throw new CriticalDependencyException(source, result.StatusCode, result.FailureReason);

            case DependencyCriticality.Important:
                _warnings.Add(
                    result.StatusCode == 501
                        ? BffWarning.NotConfiguredFor(source)
                        : BffWarning.Unavailable(source));
                return default;

            default:
                // Optionnelle : silence. Le champ vaut null, le client masque.
                return default;
        }
    }

    /// <summary>Clôt l'agrégation : mesure la durée totale et emballe la réponse.</summary>
    public BffEnvelope<T> Complete<T>(T data)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;

        BffTelemetry.RequestDuration.Record(
            elapsed, new KeyValuePair<string, object?>("bff.screen", Screen));

        if (_warnings.Count > 0)
        {
            BffTelemetry.PartialResponses.Add(
                1, new KeyValuePair<string, object?>("bff.screen", Screen));
        }

        _activity?.SetTag("bff.warnings", _warnings.Count);

        return new BffEnvelope<T>(data, _warnings);
    }

    public void Dispose() => _activity?.Dispose();
}
