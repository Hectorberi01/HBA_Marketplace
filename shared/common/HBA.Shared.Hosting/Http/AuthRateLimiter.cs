using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Shared.Hosting.Http;

/// <summary>Limitation de débit interne à un service.</summary>
public static class AuthRateLimiter
{
    /// <summary>Politique stricte : connexion, inscription, réinitialisation.</summary>
    public const string PolicyName = "auth";

    /// <summary>Politique générale, appliquée à tout le reste.</summary>
    public const string GlobalPolicyName = "global";

    // 30/min et non 10 : derrière un CGNAT — la norme au Bénin — dix personnes se
    // connectant dans la même minute ne sont pas une attaque, c'est une heure de
    // pointe.
    private const int AuthPermitPerWindow = 30;
    private static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(1);

    // Un utilisateur qui navigue fait 30 à 60 appels/minute.
    private const int GlobalPermitPerWindow = 300;
    private static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAuthRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Endpoints d'authentification : anonymes par nature, donc
            // partitionnables uniquement par adresse.
            options.AddPolicy(PolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"auth:{ClientIp(httpContext)}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = AuthPermitPerWindow,
                        Window = AuthWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(GlobalPolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = GlobalPermitPerWindow,
                        Window = GlobalWindow,
                        QueueLimit = 0
                    }));

            // Filet appliqué à TOUT endpoint qui ne déclare pas sa propre politique
            // — sans quoi seules les routes explicitement décorées seraient
            // protégées, et l'oubli passerait inaperçu.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = GlobalPermitPerWindow,
                        Window = GlobalWindow,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    /// <summary>L'identifiant de compte dès qu'il existe, l'adresse sinon.</summary>
    private static string PartitionKey(HttpContext httpContext)
    {
        var subject = httpContext.User.FindFirstValue("sub")
                      ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrWhiteSpace(subject)
            ? $"ip:{ClientIp(httpContext)}"
            : $"sub:{subject}";
    }

    private static string ClientIp(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
