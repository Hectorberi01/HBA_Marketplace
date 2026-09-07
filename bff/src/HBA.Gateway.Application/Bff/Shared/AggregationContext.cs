using System.Diagnostics;
using HBA.Gateway.Application.Abstractions.Services;

namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>Accompagne une agrégation : trace, mesure, et applique la criticité.</summary>
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

    /// <summary>Lance un appel de dépendance en le traçant et en le mesurant.</summary>
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
            span?.SetStatus(ActivityStatusCode.Error, result.FailureReason);
        }

        return result;
    }

    /// <summary>Applique la criticité à un résultat déjà obtenu.</summary>
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
