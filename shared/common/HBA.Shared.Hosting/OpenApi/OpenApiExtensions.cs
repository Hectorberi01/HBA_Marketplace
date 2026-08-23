using System.Reflection;
using Microsoft.OpenApi.Models;

namespace HBA.Shared.Hosting.OpenApi;

/// <summary>
/// Documentation OpenAPI d'un service. Section de configuration : « OpenApi ».
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
    /// ordinaire ne permettrait pas de distinguer « faux parce qu'on l'a demandé »
    /// de « faux parce qu'absent », et l'on ne saurait pas si une page fermée en
    /// recette est un choix ou un oubli.
    /// </remarks>
    public bool? Enabled { get; set; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// OPENAPI POUR LES QUATORZE SERVICES — CE QUE LA PASSERELLE NE PEUT PAS DONNER.
///
/// LA PAGE DE LA PASSERELLE NE MONTRE QUE SES AGRÉGATIONS BFF.
///
/// Son propre encadré le dit : YARP inscrit ses points de terminaison SANS
/// métadonnée d'exploration d'API — il transporte des octets, il ne sait rien des
/// paramètres ni des réponses. `/api/v1/catalog/*`, `/api/v1/auth/*`,
/// `/api/orders/*` n'y figurent donc pas, et aucun réglage de Swashbuckle n'y
/// changera rien.
///
/// La conséquence pratique, jusqu'à ce fichier : la surface HTTP réelle de la
/// plateforme — plusieurs centaines de routes — n'était documentée NULLE PART.
/// Écrire un client mobile supposait de lire `*Endpoints.cs`.
///
/// POSÉ DANS LE SOCLE, DONC SUR LES QUATORZE SERVICES À LA FOIS.
///
/// Même raison que pour la `FallbackPolicy` et la télémétrie : un branchement à
/// faire quatorze fois est un branchement qu'on oublie une fois. Ici l'oubli est
/// bénin — une page manquante — mais il serait invisible, et l'on conclurait que
/// le service n'a pas de routes documentables.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string RoutePrefix = "docs";
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// Enregistre le générateur. Appelé par <c>AddHbaService</c>.
    /// </summary>
    /// <param name="applicationAssembly">
    /// Assembly de la couche Application du service, pour ses commentaires XML.
    ///
    /// C'EST ELLE QUI PORTE LES SCHÉMAS, PAS CELLE DE L'HÔTE.
    ///
    /// Les routes sont dans `*.Api` ; les DTO — donc les descriptions de champs,
    /// les unités, les avertissements sur ce qui est nul et pourquoi — sont dans
    /// `*.Application` et `*.Contracts`. Ne charger que l'assembly de l'hôte
    /// documenterait les routes et laisserait chaque schéma nu.
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
            //
            // Sans cette déclaration, le bouton « Authorize » n'apparaît pas et
            // toutes les routes protégées répondent 401 : la page devient une liste
            // morte, qu'on ne peut que lire.
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
            //
            // Les commentaires XML ne sont produits que si le `.csproj` porte
            // `GenerateDocumentationFile`. Tous ne l'ont pas encore. Une page moins
            // riche est un désagrément ; un service qui refuse de démarrer parce
            // qu'un fichier de documentation manque est une panne.
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

    /// <summary>
    /// Sert la documentation, si la configuration l'autorise.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// FERMÉE HORS DEVELOPMENT, PAR DÉFAUT.
    ///
    /// Un service n'est pas censé être joignable depuis l'extérieur — la passerelle
    /// est le seul point d'entrée public. « Pas censé » n'est pas « ne peut pas » :
    /// un port publié par erreur, une règle de pare-feu trop large, et la carte
    /// complète de la surface interne devient lisible, routes d'administration
    /// comprises.
    ///
    /// `OpenApi:Enabled=true` permet de l'ouvrir sciemment sur un environnement
    /// fermé au réseau.
    ///
    /// `AllowAnonymous` N'EST PAS UNE FACILITÉ : SANS LUI, LA PAGE EST
    ///    INATTEIGNABLE.
    ///
    /// `AddHbaService` pose une politique de repli qui exige un compte authentifié
    /// sur tout point de terminaison ne déclarant rien. La page de documentation en
    /// fait partie : elle répondrait 401 avant même d'avoir pu servir le bouton
    /// « Authorize » qui permet de s'authentifier. On tourne en rond, et rien dans
    /// le message ne l'explique.
    ///
    /// CE N'EST PAS UN TROU : la page expose la SURFACE, jamais les données.
    /// Chaque route documentée continue d'appliquer sa propre politique — un
    /// visiteur voit qu'il existe un `POST /admin/brands`, et reçoit 403 s'il
    /// l'essaie.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
