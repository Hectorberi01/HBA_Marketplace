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

        // LES OPTIONS SONT RÉSOLUES PAR LE CONTENEUR, PAS LUES ICI.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptionsMonitor<GatewayAuthenticationOptions>>((bearer, monitor) =>
            {
                var options = monitor.CurrentValue;

                // `MapInboundClaims = false` : ON GARDE LES NOMS DU JETON.
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
                    // accepté pendant tout ce temps, y compris après une
                    // déconnexion.
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
                        // n'obtient qu'un 401 nu.
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
