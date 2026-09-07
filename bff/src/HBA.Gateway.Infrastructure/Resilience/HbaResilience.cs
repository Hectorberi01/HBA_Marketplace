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
    /// <summary>Applique délai global, réessais, disjoncteur et délai par tentative.</summary>
    public static IHttpClientBuilder AddHbaResilience(
        this IHttpClientBuilder builder, OutboundOptions options)
    {
        builder.AddResilienceHandler("hba-outbound", pipeline =>
        {
            // L'ordre compte : le délai TOTAL englobe les réessais.
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

    /// <summary>LE RÉESSAI EST INTERDIT DÈS QUE LA MÉTHODE N'EST PAS SÛRE.</summary>
    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        var method = outcome.Result?.RequestMessage?.Method;

        if (method is null || (method != HttpMethod.Get && method != HttpMethod.Head))
        {
            return false;
        }

        return IsTransient(outcome);
    }

    /// <summary>Panne passagère : rien n'indique que rejouer aboutirait au même résultat.</summary>
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
