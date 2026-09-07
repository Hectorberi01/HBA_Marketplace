using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using System.Collections.Concurrent;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>Disjoncteur par service appelé, posé sur les appels gRPC sortants.</summary>
public sealed class DisjoncteurClientInterceptor : Interceptor
{
    /// <summary>
    /// Part d'échecs à partir de laquelle on coupe, sur la fenêtre d'observation.
    /// </summary>
    public const double PartDEchecs = 0.5;

    /// <summary>Fenêtre glissante d'observation.</summary>
    public static readonly TimeSpan FenetreDObservation = TimeSpan.FromSeconds(30);

    /// <summary>Nombre d'appels en dessous duquel on ne conclut rien.</summary>
    public const int AppelsMinimum = 10;

    /// <summary>Durée de coupure.</summary>
    public static readonly TimeSpan DureeDeCoupure = TimeSpan.FromSeconds(15);

    private readonly ILogger<DisjoncteurClientInterceptor> _journal;

    private readonly ConcurrentDictionary<string, Disjoncteur> _parService = new(StringComparer.Ordinal);

    public DisjoncteurClientInterceptor(ILogger<DisjoncteurClientInterceptor> journal)
        => _journal = journal;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var service = context.Method.ServiceName;
        var disjoncteur = _parService.GetOrAdd(service, Construire);

        if (disjoncteur.Etat.CircuitState is CircuitState.Open or CircuitState.Isolated)
        {
            return Coupe<TResponse>(service, context.Method.Name);
        }

        var appel = continuation(request, context);

        // L'APPEL EST DÉJÀ PARTI ; LE PIPELINE N'ENCADRE QUE SON ATTENTE.
        var reponse = Attendre(disjoncteur, appel, service, context.Method.Name);

        return new AsyncUnaryCall<TResponse>(
            reponse,
            appel.ResponseHeadersAsync,
            appel.GetStatus,
            appel.GetTrailers,
            appel.Dispose);
    }

    private async Task<TResponse> Attendre<TResponse>(
        Disjoncteur disjoncteur, AsyncUnaryCall<TResponse> appel, string service, string rpc)
    {
        try
        {
            return await disjoncteur.Pipeline
                .ExecuteAsync(async _ => await appel.ResponseAsync.ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (BrokenCircuitException)
        {
            _journal.LogWarning(
                "Disjoncteur ouvert pour {Service} : {Rpc} refusé sans appel.", service, rpc);

            throw new RpcException(StatutDeCoupure(service));
        }
    }

    /// <summary>L'appel refusé sans avoir été émis.</summary>
    private AsyncUnaryCall<TResponse> Coupe<TResponse>(string service, string rpc)
    {
        _journal.LogWarning(
            "Disjoncteur ouvert pour {Service} : {Rpc} refusé sans appel.", service, rpc);

        var statut = StatutDeCoupure(service);

        return new AsyncUnaryCall<TResponse>(
            Task.FromException<TResponse>(new RpcException(statut)),
            Task.FromResult(new Metadata()),
            () => statut,
            () => new Metadata(),
            () => { });
    }

    private static Status StatutDeCoupure(string service)
        => new(StatusCode.Unavailable,
            $"Disjoncteur ouvert : {service} est considéré en panne, l'appel n'a pas été émis.");

    private Disjoncteur Construire(string service)
    {
        var etat = new CircuitBreakerStateProvider();

        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = PartDEchecs,
                SamplingDuration = FenetreDObservation,
                MinimumThroughput = AppelsMinimum,
                BreakDuration = DureeDeCoupure,
                StateProvider = etat,
                ShouldHandle = arguments => ValueTask.FromResult(EstUnePanne(arguments.Outcome.Exception))
            })
            .Build();

        return new Disjoncteur(pipeline, etat);
    }

    /// <summary>Ce qui compte comme une panne du service appelé.</summary>
    public static bool EstUnePanne(Exception? exception)
        => exception is RpcException rpc
            && rpc.StatusCode is StatusCode.Unavailable
                or StatusCode.DeadlineExceeded
                or StatusCode.ResourceExhausted
                or StatusCode.Internal
                or StatusCode.DataLoss
                or StatusCode.Unknown;

    private sealed record Disjoncteur(ResiliencePipeline Pipeline, CircuitBreakerStateProvider Etat);
}
