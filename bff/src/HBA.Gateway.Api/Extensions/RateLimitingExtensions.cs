using System.Globalization;
using System.Threading.RateLimiting;
using HBA.Gateway.Api.Middlewares;
using HBA.Gateway.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HBA.Gateway.Api.Extensions;

public static class RateLimitingExtensions
{
    public const string GlobalPolicy = "global";
    public const string AuthPolicy = "auth";
    public const string OtpPolicy = "otp";
    public const string ReadPolicy = "read";
    public const string WritePolicy = "write";

    /// <summary>Type de contenu des erreurs de la passerelle (§35).</summary>
    public const string ProblemJson = "application/problem+json";

    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        // LES LIMITES SONT LUES À LA REQUÊTE, PAS À L'ENREGISTREMENT.
        services
            .AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Filet global : s'applique en plus de la politique nommée de la route.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => Partition(context, GlobalPolicy, Current(context).Global));

            limiter.AddPolicy(AuthPolicy, context => Partition(context, AuthPolicy, Current(context).Auth));
            limiter.AddPolicy(OtpPolicy, context => Partition(context, OtpPolicy, Current(context).Otp));
            limiter.AddPolicy(ReadPolicy, context => Partition(context, ReadPolicy, Current(context).Read));
            limiter.AddPolicy(WritePolicy, context => Partition(context, WritePolicy, Current(context).Write));

            limiter.OnRejected = async (OnRejectedContext context, CancellationToken cancellationToken) =>
            {
                // Un refus doit avoir la même forme que toute autre erreur de la
                // passerelle : le client n'a alors qu'un seul format à traiter.

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    // Sans `Retry-After`, une application mobile réessaie
                    // immédiatement — et le refus produit plus de trafic que la
                    // requête qu'il a bloquée.
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Type = "https://api.hba-express.com/errors/too-many-requests",
                    Title = "Too Many Requests",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = "Trop de requêtes. Réessayez dans quelques instants.",
                    Instance = context.HttpContext.Request.Path
                };

                problem.Extensions["correlationId"] =
                    context.HttpContext.Items[CorrelationIdMiddleware.HeaderName]?.ToString();


                // LE TYPE DE CONTENU EST PASSÉ À L'ÉCRITURE, ET NON AVANT.
                await context.HttpContext.Response.WriteAsJsonAsync(
                    problem,
                    options: null,
                    contentType: ProblemJson,
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>Les limites en vigueur, résolues depuis le conteneur.</summary>
    private static RateLimitingOptions Current(HttpContext context)
        => context.RequestServices
            .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
            .CurrentValue;

    private static RateLimitPartition<string> Partition(
        HttpContext context, string policyName, RateLimitPolicyOptions options)
        => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{policyName}:{PartitionKey(context)}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = TimeSpan.FromSeconds(options.WindowSeconds),

                // AUCUNE FILE D'ATTENTE, DÉLIBÉRÉMENT.
                QueueLimit = 0,
                AutoReplenishment = true
            });

    /// <summary>LA CLÉ DE PARTITION EST LE POINT SENSIBLE DE TOUT LE MÉCANISME.</summary>
    private static string PartitionKey(HttpContext context)
    {
        var subject = context.User.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"sub:{subject}";
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
