using System.Reflection;
using Microsoft.OpenApi.Models;

namespace HBA.Shared.Hosting.OpenApi;

/// <summary>Documentation OpenAPI d'un service.</summary>
public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    /// <summary>La documentation est-elle servie ?</summary>
    public bool? Enabled { get; set; }
}

/// <summary>OPENAPI POUR LES QUATORZE SERVICES — CE QUE LA PASSERELLE NE PEUT PAS DONNER.</summary>
public static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string RoutePrefix = "docs";
    private const string BearerScheme = "Bearer";

    /// <summary>Enregistre le générateur.</summary>
    /// <param name="applicationAssembly">
    /// Assembly de la couche Application du service, pour ses commentaires XML.
    /// </param>
    public static IServiceCollection AddHbaOpenApi(
        this IServiceCollection services, string serviceName, Assembly applicationAssembly)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = $"HBA — {serviceName}",
                Version = "v1",
                Description =
                    "Surface HTTP de ce service, telle qu'elle est servie DERRIÈRE la "
                    + "passerelle.\n\n"
                    + "Les chemins affichés ici sont ceux du service. La passerelle les "
                    + "expose sous le même chemin — elle ne réécrit que les anciens préfixes "
                    + "dépréciés (voir D15).\n\n"
                    + "Chaque réponse est enveloppée (§25) : `success`, puis `data` OU "
                    + "`error`, plus `meta`. `meta.requestId` est l'identifiant à citer dans "
                    + "un signalement — il est présent en succès comme en échec."
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
                    "Jeton émis par identity-service (`POST /api/v1/auth/login`). "
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

            // TROIS ASSEMBLIES CANDIDATES, ET ON NE LÈVE PAS SI LE XML MANQUE.
            foreach (var assembly in new[]
                     {
                         Assembly.GetEntryAssembly(),
                         applicationAssembly
                     })
            {
                var nom = assembly?.GetName().Name;
                if (nom is null)
                {
                    continue;
                }

                var xml = Path.Combine(AppContext.BaseDirectory, $"{nom}.xml");

                if (File.Exists(xml))
                {
                    options.IncludeXmlComments(xml);
                }
            }
        });

        return services;
    }

    /// <summary>Sert la documentation, si la configuration l'autorise.</summary>
    public static WebApplication UseHbaOpenApi(this WebApplication app, string serviceName)
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
            ui.SwaggerEndpoint($"/swagger/{DocumentName}/swagger.json", $"HBA — {serviceName} v1");
            ui.RoutePrefix = RoutePrefix;
            ui.DocumentTitle = $"HBA — {serviceName}";
            ui.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        });

        app.Logger.LogInformation(
            "Documentation OpenAPI de {Service} servie sur /{Prefix}.", serviceName, RoutePrefix);

        return app;
    }
}
