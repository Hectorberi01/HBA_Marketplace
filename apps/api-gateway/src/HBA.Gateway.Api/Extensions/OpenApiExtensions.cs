using System.Reflection;
using Microsoft.OpenApi.Models;

namespace HBA.Gateway.Api.Extensions;

/// <summary>
/// Documentation OpenAPI de la passerelle. Section de configuration : « OpenApi ».
/// </summary>
public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    /// <summary>
    /// La documentation est-elle servie ?
    /// </summary>
    /// <remarks>
    /// Booléen NULLABLE : `null` signifie « non renseigné », et le défaut dépend
    /// alors de l'environnement — vrai en Development, faux ailleurs. Un `bool`
    /// ordinaire ne permettrait pas de distinguer « faux parce qu'on l'a
    /// demandé » de « faux parce qu'absent ».
    /// </remarks>
    public bool? Enabled { get; set; }
}

public static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string RoutePrefix = "docs";
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// Enregistre le générateur OpenAPI.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SEULS LES BFF APPARAÎTRONT. LES ROUTES PROXIFIÉES, NON.
    ///
    /// C'est la première question que posera quiconque ouvre cette page : où sont
    /// `/api/auth/*`, `/api/food/*`, `/api/orders/*` ?
    ///
    /// Elles sont servies par YARP, qui inscrit ses points de terminaison SANS
    /// métadonnées d'exploration d'API — il ne sait rien de leurs paramètres ni
    /// de leurs réponses, il transporte des octets. `ApiExplorer` ne les voit
    /// donc pas, et aucune configuration de Swashbuckle n'y changera quoi que ce
    /// soit.
    ///
    /// Ce document décrit ce que la passerelle CALCULE — les agrégations BFF —
    /// et non ce qu'elle RELAIE. Pour les routes relayées, la référence est la
    /// documentation du service amont.
    ///
    /// Le jour où l'on voudra un document unifié, il faudra agréger les documents
    /// OpenAPI des treize services au moment de la construction, pas espérer que
    /// le proxy les devine.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
            //
            // Sans cette déclaration, le bouton « Authorize » n'apparaît pas et
            // toutes les routes répondent 401 : la page devient une liste morte.
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
            //
            // DEUX FICHIERS, ET LE SECOND EST LE PLUS UTILE.
            //
            // Les contrôleurs sont dans Api ; les DTO — donc les descriptions de
            // champs, les unités, les avertissements sur ce qui est nul et
            // pourquoi — sont dans Application. Ne charger que celui de Api
            // documenterait les routes et laisserait chaque schéma nu.
            foreach (var assembly in new[] { typeof(Program).Assembly, ApplicationAssembly() })
            {
                var xml = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");

                if (File.Exists(xml))
                {
                    options.IncludeXmlComments(xml, includeControllerXmlComments: true);
                }
            }

            // PAS DE `CustomSchemaIds` ICI, ET C'EST UN CHOIX.
            //
            // Le réflexe est d'écrire `CustomSchemaIds(t => t.FullName)` pour
            // parer aux homonymes entre BFF. Ce serait faux ici : toutes les
            // réponses sont des `BffEnvelope<T>`, et le `FullName` d'un type
            // générique est une chaîne du genre
            // `…BffEnvelope`1[[…MerchantDashboardDto, HBA.Gateway.Application,
            // Version=1.0.0.0, Culture=neutral…]]` — illisible dans la page, et
            // porteuse de caractères que la spécification n'admet pas.
            //
            // Le nom court par défaut suffit tant qu'il n'y a pas d'homonyme, et
            // il n'y en a aucun : vérifié sur les DTO des cinq BFF. Le jour où
            // deux types se disputeront un nom, Swashbuckle lèvera au démarrage
            // en les nommant tous les deux — un échec net, au bon moment.
        });

        return services;
    }

    /// <summary>
    /// Sert la documentation, si la configuration l'autorise.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// FERMÉE HORS DEVELOPMENT, PAR DÉFAUT.
    ///
    /// La passerelle est le point d'entrée PUBLIC de la plateforme. Y publier la
    /// liste de ses routes, de ses paramètres et de ses schémas donne à un
    /// attaquant la carte qu'il devrait passer des heures à reconstituer — et
    /// signale au passage les routes d'administration.
    ///
    /// `OpenApi:Enabled=true` permet de l'ouvrir sciemment, par exemple sur un
    /// environnement de recette fermé au réseau.
    ///
    /// `AllowAnonymous` N'EST PAS UNE FACILITÉ : SANS LUI, LA PAGE EST
    ///    INATTEIGNABLE.
    ///
    /// `AddGatewayAuthorization` pose une politique de repli qui exige un compte
    /// authentifié sur tout point de terminaison ne déclarant rien. La page de
    /// documentation en fait partie : elle répondrait 401 avant même d'avoir pu
    /// servir le bouton « Authorize » qui permet de s'authentifier. On tourne en
    /// rond, et rien dans le message ne l'explique.
    ///
    /// Ce que la page expose reste la SURFACE, jamais les données : chaque route
    /// documentée continue d'appliquer sa propre politique.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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

    /// <summary>
    /// Assembly de la couche Application, atteinte par un type qu'elle publie.
    /// </summary>
    /// <remarks>
    /// `typeof(BffEnvelope&lt;&gt;)` plutôt qu'un chargement par nom : une chaîne
    /// de caractères survivrait à un renommage d'assembly sans rien dire, et les
    /// schémas se retrouveraient nus sans qu'on sache pourquoi.
    /// </remarks>
    private static Assembly ApplicationAssembly()
        => typeof(Application.Bff.Shared.BffEnvelope<>).Assembly;
}
