using System.Text;
using HBA.Shared.Infrastructure.Hosting;
using HBA.Shared.Application;
using MediatR;
using HBA.Shared.Infrastructure;
using HBA.Shared.Hosting.Http;
using HBA.Shared.Hosting.OpenApi;
using HBA.Shared.Hosting.Telemetry;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace HBA.Shared.Hosting;

/// <summary>
/// Amorçage commun aux treize services : configuration, module, authentification,
/// corrélation, erreurs, sondes.
/// </summary>
public static class ServiceHostExtensions
{
    /// <summary>Enregistre le socle d'un service et son module métier.</summary>
    /// <typeparam name="TDbContext">DbContext du service, sondé par /health/ready.</typeparam>
    public static WebApplicationBuilder AddHbaService<TDbContext>(
        this WebApplicationBuilder builder, IModuleInstaller installer)
        where TDbContext : DbContext
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddProblemDetails();
        services.AddHttpContextAccessor();

        services.Configure<InternalCallOptions>(
            configuration.GetSection(InternalCallOptions.SectionName));

        // UNE SEULE ASSEMBLY SCANNÉE, ET C'EST TOUTE LA DIFFÉRENCE.
        services.AddMediatR(mediator =>
            mediator.RegisterServicesFromAssembly(installer.ApplicationAssembly));

        services.AddBuildingBlocksPipeline();

        // La configuration décide du cache distribué : Redis s'il est renseigné,
        // mémoire sinon — et le repli s'annonce au démarrage.
        services.AddBuildingBlocksInfrastructure(configuration);

        installer.Install(services, configuration);

        ConfigureForwardedHeaders(services);

        AddAuthentication(services, configuration);

        // LE FILET QUI MANQUAIT : SANS LUI, UN `MapGroup` NU EST ANONYME.
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        // SANS CET ENREGISTREMENT, `RequireRateLimiting("auth")` LÈVE AU DÉMARRAGE.
        services.AddAuthRateLimiter();

        services
            .AddHealthChecks()
            .AddDbContextCheck<TDbContext>("database", tags: ["ready"]);

        // OPENAPI — LA SURFACE RÉELLE DE LA PLATEFORME N'ÉTAIT DOCUMENTÉE NULLE
        // PART.
        services.AddHbaOpenApi(
            FirstNonEmpty(configuration["SERVICE_NAME"], configuration["ServiceName"], "service"),
            installer.ApplicationAssembly);

        // INSTRUMENTATION — POSÉE ICI, DONC SUR LES QUATORZE SERVICES À LA FOIS.
        builder.AddHbaTelemetry(FirstNonEmpty(
            configuration["SERVICE_NAME"],
            configuration["ServiceName"],
            "unknown-service"));

        return builder;
    }

    /// <summary>Pipeline commun. L'ordre est celui de la passerelle, pour la même raison.</summary>
    /// <param name="serviceName">Nom du service, ex. `user-service`.</param>
    /// <param name="serviceCode">
    /// Préfixe des codes `&lt;SERVICE&gt;_SERVICE_NOT_FOUND` du §10.
    /// </param>
    public static WebApplication UseHbaService(
        this WebApplication app, string? serviceName = null, string? serviceCode = null)
    {
        // AVANT TOUT LE RESTE : c'est lui qui remplace l'adresse de la passerelle
        // par celle du client.
        if (!string.Equals(app.Configuration["ProxyTrust:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            app.UseForwardedHeaders();
        }

        // Intercepteur d'abord : il ne protège que ce qui le suit.
        app.UseMiddleware<ServiceExceptionMiddleware>();
        app.UseMiddleware<ServiceCorrelationMiddleware>();

        // LA DOCUMENTATION AVANT `UseAuthorization`, ET L'ORDRE EST LA RAISON POUR
        // LAQUELLE ELLE FONCTIONNE.
        app.UseHbaOpenApi(FirstNonEmpty(
            serviceName,
            app.Configuration["SERVICE_NAME"],
            app.Configuration["ServiceName"],
            "service"));

        app.UseAuthentication();

        // Après l'authentification : sans cela, la partition par claim `sub` ne
        // verrait jamais l'utilisateur et retomberait sur l'adresse IP — donc sur
        // le CGNAT, ce que la partition par compte sert précisément à éviter.
        app.UseRateLimiter();

        app.UseAuthorization();

        // CONTEXTE PROPAGÉ DU §18 — APRÈS L'AUTHENTIFICATION, ET C'EST L'ESSENTIEL.
        var resolvedName = FirstNonEmpty(
            serviceName,
            app.Configuration["SERVICE_NAME"],
            app.Configuration["ServiceName"],
            "unknown-service");

        app.UseHbaRequestContext(resolvedName, serviceCode);

        // `AllowAnonymous` explicite : sans lui, Docker reçoit 401, déclare le
        // conteneur malsain et le redémarre en boucle sans erreur applicative.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            // POUR UN SERVICE, LA BASE EST CRITIQUE — CONTRAIREMENT À LA
            // PASSERELLE.
            Predicate = check => check.Tags.Contains("ready")
        }).AllowAnonymous();

        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        return app;
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate!;
            }
        }

        return "unknown-service";
    }

    /// <summary>LE SOCLE DE SÉCURITÉ SEUL, POUR LES HÔTES SANS BASE DE DONNÉES.</summary>
    public static WebApplicationBuilder AddHbaSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        ConfigureForwardedHeaders(services);

        services.AddProblemDetails();
        services.AddHttpContextAccessor();

        services.Configure<InternalCallOptions>(
            configuration.GetSection(InternalCallOptions.SectionName));

        AddAuthentication(services, configuration);

        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return builder;
    }

    /// <summary>Le pendant de <see cref="AddHbaSecurity"/> dans le pipeline.</summary>
    public static WebApplication UseHbaSecurity(this WebApplication app)
    {
        // Même réglage que le socle complet : `AddHbaSecurity` a configuré les
        // en-têtes de mandataire, il faut encore les appliquer.
        if (!string.Equals(app.Configuration["ProxyTrust:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            app.UseForwardedHeaders();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    /// <summary>L'ADRESSE DU CLIENT, ET NON CELLE DE LA PASSERELLE.</summary>
    private static void ConfigureForwardedHeaders(IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Par défaut la bibliothèque ne fait confiance qu'au bouclage, ce qui
            // exclut la passerelle : on vide, puis on déclare les plages privées.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();

            // `new(...)` CIBLÉ, ET NON `new IPNetwork(...)`.
            options.KnownNetworks.Add(new(IPAddress.Parse("10.0.0.0"), 8));
            options.KnownNetworks.Add(new(IPAddress.Parse("172.16.0.0"), 12));
            options.KnownNetworks.Add(new(IPAddress.Parse("192.168.0.0"), 16));
            options.KnownNetworks.Add(new(IPAddress.Parse("127.0.0.0"), 8));

            // Une seule couche de mandataire entre le client et le service.
            options.ForwardLimit = 1;
        });
    }

    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Issuer"];
        var audience = configuration["Authentication:Audience"];
        var signingKey = configuration["Authentication:SigningKey"];

        // UNE CLÉ DE SIGNATURE ABSENTE N'EST PAS UNE CONFIGURATION PARTIELLE.
        if (string.IsNullOrWhiteSpace(signingKey)
            && EnvironnementDeploiement.EstProduction(configuration))
        {
            throw new InvalidOperationException(
                "Authentication:SigningKey est absente. Le service démarrerait en "
                + "rejetant TOUS les jetons — `ValidateIssuerSigningKey` reste actif "
                + "sans clé à comparer — et chaque appel authentifié rendrait 401 "
                + "sans qu'aucune erreur de démarrage ne l'explique. "
                + "Renseigner AUTHENTICATION__SIGNINGKEY.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // Cinq minutes par défaut chez .NET : un jeton révoqué
                    // resterait accepté pendant tout ce temps.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = "sub",
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                if (!string.IsNullOrWhiteSpace(signingKey))
                {
                    bearer.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

                    // Épinglage : ferme la confusion d'algorithme (`alg: none`,
                    // bascule vers un algorithme asymétrique à clé choisie).
                    bearer.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
                }
            });

        // LE SERVICE REVALIDE LE JETON, MÊME DERRIÈRE LA PASSERELLE.
    }
}
