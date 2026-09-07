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

    /// <summary>
    /// Type de contenu des erreurs de la passerelle (§35).
    /// </summary>
    /// <remarks>
    /// Constante partagée avec <c>ExceptionMiddleware</c> : une erreur de débit et
    /// une erreur interne doivent avoir EXACTEMENT la même forme, sans quoi le
    /// client a deux formats à traiter là où le cahier n'en prévoit qu'un.
    /// </remarks>
    public const string ProblemJson = "application/problem+json";

    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ═════════════════════════════════════════════════════════════════════
        // LES LIMITES SONT LUES À LA REQUÊTE, PAS À L'ENREGISTREMENT.
        //
        // La version précédente faisait ici :
        //
        //     var options = configuration.GetSection(...).Get<RateLimitingOptions>();
        //
        // — et distribuait cet objet aux cinq politiques. La lecture avait donc
        // lieu pendant que `Program.cs` composait l'application : toute source de
        // configuration ajoutée ensuite était ignorée.
        //
        // Symptôme observé : `WebApplicationFactory` abaissait `Auth.PermitLimit`
        // à 3 pour un test, la passerelle gardait 10, et six tentatives de
        // connexion ne déclenchaient jamais le refus. Le test qui PROTÈGE
        // `/api/auth/login` contre l'énumération de mots de passe était donc
        // rouge — c'est-à-dire que plus personne ne surveillait cette protection.
        //
        // LA LIMITE D'UNE PARTITION RESTE FIGÉE À SA CRÉATION.
        //
        // `GetFixedWindowLimiter` n'appelle sa fabrique qu'une fois par clé. Un
        // changement de configuration s'applique donc aux partitions FUTURES, pas
        // à celles déjà ouvertes. C'est acceptable — la fenêtre les fait expirer —
        // mais il ne faut pas en attendre un rechargement immédiat.
        // ═════════════════════════════════════════════════════════════════════
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
                // Le type est posé à l'écriture — cf. plus bas.

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


                // ═════════════════════════════════════════════════════════════
                // LE TYPE DE CONTENU EST PASSÉ À L'ÉCRITURE, ET NON AVANT.
                //
                // `WriteAsJsonAsync(value, cancellationToken)` ÉCRASE
                // `Response.ContentType` par « application/json; charset=utf-8 ».
                // Le poser au-dessus ne sert donc à rien : la réponse partait en
                // `application/json` malgré la ligne qui demandait
                // `application/problem+json`.
                //
                // Défaut resté invisible jusqu'ici parce que le test qui le
                // vérifiait s'arrêtait plus tôt — la limite n'étant jamais
                // atteinte, l'assertion sur le type n'était jamais exécutée. Un
                // défaut en masquait un autre.
                // ═════════════════════════════════════════════════════════════
                await context.HttpContext.Response.WriteAsJsonAsync(
                    problem,
                    options: null,
                    contentType: ProblemJson,
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Les limites en vigueur, résolues depuis le conteneur.
    /// </summary>
    /// <remarks>
    /// `CurrentValue` EST MIS EN CACHE PAR `IOptionsMonitor`.
    ///
    /// Ce n'est donc pas une relecture de configuration à chaque requête : le
    /// moniteur conserve la valeur liée et ne la recalcule qu'au changement d'une
    /// source. Le coût est celui d'une résolution de service, sur un chemin qui
    /// en fait déjà plusieurs.
    /// </remarks>
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
                //
                // Une file fait patienter le client au lieu de le refuser : la
                // requête aboutit, mais après un délai qui s'ajoute au sien. Sous
                // charge, cela transforme une limitation nette en latence diffuse,
                // beaucoup plus difficile à diagnostiquer qu'une rafale de 429.
                QueueLimit = 0,
                AutoReplenishment = true
            });

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA CLÉ DE PARTITION EST LE POINT SENSIBLE DE TOUT LE MÉCANISME.
    ///
    /// Trop large, la limite se partage : derrière un ingress, `RemoteIpAddress`
    /// est celle du répartiteur de charge, et les 300 requêtes/minute deviennent
    /// un compteur unique pour la plateforme entière — le premier utilisateur
    /// actif bloque tous les autres.
    ///
    /// Falsifiable, elle ne limite rien : si l'on faisait confiance à
    /// `X-Forwarded-For` sans liste de proxys de confiance, il suffirait de
    /// changer d'en-tête à chaque requête pour obtenir une partition neuve.
    ///
    /// D'où l'ordre suivant :
    ///   1. le claim `sub` du jeton — signé, donc infalsifiable, et stable même
    ///      si l'utilisateur change de réseau en pleine commande ;
    ///   2. à défaut, l'adresse de connexion, qui n'est fiable que si
    ///      `UseForwardedHeaders` est configuré avec des proxys connus (voir
    ///      `ForwardedHeadersOptions` dans Program.cs).
    ///
    /// Conséquence assumée : le trafic anonyme derrière un même NAT partage sa
    /// partition. C'est acceptable pour `login` — c'est même l'effet recherché
    /// contre l'énumération de mots de passe — et c'est la raison pour laquelle
    /// la politique `read` est large.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
