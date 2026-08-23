using System.Net;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace HBA.Gateway.Infrastructure.Resilience;

/// <summary>Politique de résilience commune aux clients sortants du BFF.</summary>
public static class HbaResilience
{
    /// <summary>
    /// Applique délai global, réessais, disjoncteur et délai par tentative.
    /// </summary>
    public static IHttpClientBuilder AddHbaResilience(
        this IHttpClientBuilder builder, OutboundOptions options)
    {
        builder.AddResilienceHandler("hba-outbound", pipeline =>
        {
            // L'ordre compte : le délai TOTAL englobe les réessais. Placé après
            // la stratégie de réessai, il n'aurait borné qu'une tentative, et
            // trois tentatives de 5 s auraient donné 15 s d'attente au client.
            pipeline.AddTimeout(options.TotalTimeout);

            if (options.MaxRetryAttempts > 0)
            {
                pipeline.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200),
                    ShouldHandle = arguments => ValueTask.FromResult(ShouldRetry(arguments.Outcome))
                });
            }

            pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,
                BreakDuration = options.CircuitBreakerDuration,
                ShouldHandle = arguments => ValueTask.FromResult(IsTransient(arguments.Outcome))
            });

            pipeline.AddTimeout(options.AttemptTimeout);
        });

        return builder;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE RÉESSAI EST INTERDIT DÈS QUE LA MÉTHODE N'EST PAS SÛRE.
    ///
    /// Une réponse 500 après un `POST /api/payments` ne dit PAS que le paiement
    /// n'a pas eu lieu : elle dit que la réponse n'est pas arrivée. Rejouer, c'est
    /// débiter deux fois. Même raisonnement pour la création de commande et la
    /// demande de course.
    ///
    /// Aujourd'hui <c>IServiceClient</c> n'expose que des GET, et le trafic
    /// d'écriture passe par YARP — qui ne réessaie rien. Ce garde ne sert donc
    /// à rien… tant que personne n'ajoute `PostJsonAsync`. Le jour où quelqu'un
    /// le fera, c'est CE test-ci qui empêchera le double débit, pas une note dans
    /// un document.
    ///
    /// Conséquence assumée : lorsqu'aucune réponse n'est revenue (panne réseau,
    /// délai dépassé), la méthode est inconnue et l'on ne réessaie pas. Perdre un
    /// réessai sur une lecture coûte une lecture ; en gagner un sur un paiement
    /// coûte de l'argent réel au client.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        var method = outcome.Result?.RequestMessage?.Method;

        if (method is null || (method != HttpMethod.Get && method != HttpMethod.Head))
        {
            return false;
        }

        return IsTransient(outcome);
    }

    /// <summary>
    /// Panne passagère : rien n'indique que rejouer aboutirait au même résultat.
    /// Un 4xx en est exclu — la requête est fautive, la rejouer la refera échouer
    /// à l'identique en consommant du quota chez le service appelé.
    /// </summary>
    private static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        var response = outcome.Result;

        if (response is null)
        {
            return false;
        }

        return (int)response.StatusCode >= 500
            || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
    }
}
