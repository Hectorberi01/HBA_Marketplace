using System.Text;
using HBA.Gateway.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HBA.Gateway.Api.Extensions;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Configure la validation des jetons : clé symétrique si elle est fournie,
    /// découverte OIDC sinon.
    /// </summary>
    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<GatewayAuthenticationOptions>()
            .Bind(configuration.GetSection(GatewayAuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<GatewayAuthenticationOptions>,
            GatewayAuthenticationOptionsValidator>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // ═════════════════════════════════════════════════════════════════════
        // LES OPTIONS SONT RÉSOLUES PAR LE CONTENEUR, PAS LUES ICI.
        //
        // La version précédente faisait, juste au-dessus de cet appel :
        //
        //     var options = configuration.GetSection(...).Get<...>();
        //
        // — une lecture IMMÉDIATE de `builder.Configuration`, exécutée pendant
        // que `Program.cs` compose l'application. Toute source de configuration
        // ajoutée APRÈS ce moment était donc ignorée. Trois conséquences, dont
        // deux ne se voient pas :
        //
        //   • `WebApplicationFactory` applique ses surcharges après le point
        //     d'entrée : la clé de signature de test n'atteignait jamais la
        //     validation, et TOUS les jetons de test étaient refusés. C'est ce
        //     qui rendait `Un_jeton_valide_franchit_l_autorisation` rouge — un
        //     test qui semblait accuser l'autorisation alors que le défaut était
        //     dans la lecture de la configuration ;
        //
        //   • un `IOptionsMonitor` qui recharge n'aurait jamais rien changé ;
        //
        //   • la validation `ValidateOnStart` enregistrée dix lignes plus haut
        //     portait sur des options QUE PERSONNE N'UTILISAIT.
        //
        // `Configure<IOptionsMonitor<…>>` diffère la lecture jusqu'à la première
        // résolution des `JwtBearerOptions`, c'est-à-dire après que toutes les
        // sources sont en place.
        // ═════════════════════════════════════════════════════════════════════
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptionsMonitor<GatewayAuthenticationOptions>>((bearer, monitor) =>
            {
                var options = monitor.CurrentValue;

                // `MapInboundClaims = false` : ON GARDE LES NOMS DU JETON.
                //
                // À `true` (défaut historique), .NET renomme silencieusement les
                // claims — `sub` devient l'URI `nameidentifier`. Un service qui
                // lirait `sub` côté passerelle et `sub` côté microservice
                // n'obtiendrait donc pas la même chose des deux côtés. On garde
                // les noms tels que le jeton les porte.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,

                    ValidateAudience = true,
                    ValidAudience = options.Audience,

                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // Cinq minutes par défaut chez .NET : un jeton expiré resterait
                    // accepté pendant tout ce temps, y compris après une déconnexion.
                    ClockSkew = options.ClockSkew,

                    NameClaimType = "sub",
                    RoleClaimType = options.RoleClaimType
                };

                if (!string.IsNullOrWhiteSpace(options.SigningKey))
                {
                    // Mode actuel : identity-service signe en HMAC-SHA256.
                    bearer.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));

                    // ALGORITHME ÉPINGLÉ. NE PAS RETIRER.
                    //
                    // Sans cette liste, un jeton présenté avec `alg: none` ou avec
                    // un algorithme asymétrique dont l'attaquant choisit la clé est
                    // recevable par la bibliothèque. C'est la famille d'attaques de
                    // confusion d'algorithme, et l'épinglage est ce qui la ferme.
                    bearer.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
                }
                else
                {
                    bearer.Authority = options.Authority;
                    bearer.RequireHttpsMetadata = options.RequireHttpsMetadata;
                }

                bearer.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Le motif détaillé reste dans les journaux ; le client
                        // n'obtient qu'un 401 nu. Renvoyer « signature invalide »
                        // ou « émetteur inattendu » renseigne un attaquant sur la
                        // configuration exacte à contourner.
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("HBA.Gateway.Authentication");

                        logger.LogDebug(
                            context.Exception,
                            "Validation du jeton refusée sur {Path}", context.HttpContext.Request.Path);

                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }
}
