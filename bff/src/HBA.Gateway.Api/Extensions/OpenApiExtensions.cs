using System.Reflection;
using Microsoft.OpenApi.Models;

namespace HBA.Gateway.Api.Extensions;

/// <summary>Documentation OpenAPI de la passerelle.</summary>
public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    /// <summary>La documentation est-elle servie ?</summary>
    public bool? Enabled { get; set; }
}

public static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string RoutePrefix = "docs";
    private const string BearerScheme = "Bearer";

    /// <summary>Enregistre le générateur OpenAPI.</summary>
    public static IServiceCollection AddGatewayOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "HBA — Passerelle BFF",
                Version = "v1",
                Description =
                    "Agrégations calculées par la passerelle pour les quatre applications : "
                    + "Client Express, Client Food, Driver, Partner (Merchant et Restaurant).\n\n"
                    + "Les routes relayées vers les treize services (`/api/auth/*`, "
                    + "`/api/food/*`, `/api/orders/*`…) ne figurent PAS ici : elles sont "
                    + "servies par le proxy inverse, qui n'expose aucune métadonnée d'API. "
                    + "Se reporter à la documentation du service concerné.\n\n"
                    + "Chaque réponse BFF est enveloppée : `data` porte le résultat, "
                    + "`warnings` signale les dépendances dégradées — une réponse 200 "
                    + "accompagnée d'avertissements est une réponse PARTIELLE."
            });

            // ── Jeton porteur, pour pouvoir essayer depuis la page ──────────
            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description =
                    "Jeton émis par identity-service (`POST /api/auth/login`). "
                    + "Coller le jeton SEUL — l'interface ajoute « Bearer »."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = BearerScheme
                    }
                }] = []
            });

            // ── Commentaires XML des deux assemblies ────────────────────────
            foreach (var assembly in new[] { typeof(Program).Assembly, ApplicationAssembly() })
            {
                var xml = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");

                if (File.Exists(xml))
                {
                    options.IncludeXmlComments(xml, includeControllerXmlComments: true);
                }
            }

            // PAS DE `CustomSchemaIds` ICI, ET C'EST UN CHOIX.
        });

        return services;
    }

    /// <summary>Sert la documentation, si la configuration l'autorise.</summary>
    public static WebApplication UseGatewayOpenApi(this WebApplication app)
    {
        var options = app.Configuration
            .GetSection(OpenApiOptions.SectionName)
            .Get<OpenApiOptions>() ?? new OpenApiOptions();

        var enabled = options.Enabled ?? app.Environment.IsDevelopment();

        if (!enabled)
        {
            return app;
        }

        app.UseSwagger();

        app.UseSwaggerUI(ui =>
        {
            ui.SwaggerEndpoint($"/swagger/{DocumentName}/swagger.json", "HBA — Passerelle BFF v1");
            ui.RoutePrefix = RoutePrefix;
            ui.DocumentTitle = "HBA — Passerelle BFF";

            // Les BFF sont regroupés par audience : replier les sections évite
            // d'ouvrir sur une page de plusieurs écrans.
            ui.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        });

        app.Logger.LogInformation(
            "Documentation OpenAPI servie sur /{Prefix} (BFF uniquement — les routes "
            + "proxifiées n'y figurent pas).", RoutePrefix);

        return app;
    }

    /// <summary>Assembly de la couche Application, atteinte par un type qu'elle publie.</summary>
    private static Assembly ApplicationAssembly()
        => typeof(Application.Bff.Shared.BffEnvelope<>).Assembly;
}
