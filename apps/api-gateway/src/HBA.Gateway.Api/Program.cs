using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Middlewares;
using HBA.Gateway.Application;
using HBA.Gateway.Application.Abstractions;
using HBA.Gateway.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ═════════════════════════════════════════════════════════════════════════════
// SERVICES
// ═════════════════════════════════════════════════════════════════════════════
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services
    .AddGatewayAuthentication(builder.Configuration)
    .AddGatewayAuthorization(builder.Configuration)
    .AddGatewayRateLimiting(builder.Configuration)
    .AddGatewayReverseProxy(builder.Configuration)
    .AddGatewayHealthChecks()
    .AddGatewayTokenRevocation(builder.Configuration, builder.Environment.IsDevelopment())
    .AddGatewayOpenTelemetry(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddGatewayOpenApi();

builder.Services.AddGatewayApplication();
builder.Services.AddGatewayBffOptions(builder.Configuration);
builder.Services.AddGatewayInfrastructure(builder.Configuration);

// Porte l'identifiant de corrélation jusqu'à la couche Application sans lui
// exposer HttpContext. Les deux enregistrements visent le MÊME objet : sans
// cela, l'agrégateur lirait un support vide pendant que le middleware
// renseignerait l'autre.
builder.Services.AddScoped<CorrelationContextHolder>();
builder.Services.AddScoped<ICorrelationContext>(provider =>
    provider.GetRequiredService<CorrelationContextHolder>());

// Journal disponible avant la construction de l'hôte : les avertissements de
// confiance des proxys doivent être visibles au démarrage, pas à la première
// requête.
using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());

builder.Services.AddGatewayForwardedHeaders(
    builder.Configuration, startupLoggerFactory.CreateLogger("HBA.Gateway.Startup"));

var app = builder.Build();

// ═════════════════════════════════════════════════════════════════════════════
// PIPELINE — L'ORDRE EST UNE DÉCISION, PAS UNE MISE EN FORME.
//
// 1. ForwardedHeaders  : avant tout ce qui lit une IP ou un schéma.
// 2. Exception         : un intercepteur ne protège que ce qui le SUIT. Placé
//                        plus bas, une panne du limiteur renverrait au client la
//                        page d'erreur brute du serveur, pile d'appels comprise
//                        en Development.
// 3. Correlation       : tout ce qui suit — journaux, en-têtes sortants, traces —
//                        porte l'identifiant.
// 4. RequestLogging    : mesure le temps du reste du pipeline.
// 5. Authentication    : AVANT le limiteur, faute de quoi `PartitionKey` ne
//                        verrait jamais le claim `sub` et partitionnerait tout le
//                        trafic authentifié par IP — c'est-à-dire par NAT.
// 6. RateLimiter
// 7. Révocation       : après l'authentification — il ne travaille que sur une
//                        requête déjà authentifiée — et après le limiteur, pour
//                        qu'une rafale soit coupée avant de devenir une rafale
//                        d'appels vers identity. Avant l'autorisation : un jeton
//                        mort ne doit franchir aucune politique.
// 8. Authorization
// ═════════════════════════════════════════════════════════════════════════════
app.UseForwardedHeaders();

app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// ═════════════════════════════════════════════════════════════════════════════
// LA DOCUMENTATION PASSE AVANT L'AUTORISATION, ET CE N'EST PAS UN CONFORT.
//
// `AddGatewayAuthorization` pose une politique de REPLI qui exige un compte
// authentifié. Cette politique s'applique aussi aux requêtes qui ne
// correspondent à AUCUN point de terminaison — ce qui est le cas de `/docs` et
// de `/swagger/*`, servis par un intergiciel et non par une route.
//
// Placée après `UseAuthorization`, la page répondrait donc 401 avant même
// d'avoir pu afficher le bouton « Authorize » qui permet de s'authentifier. On
// tourne en rond, et le message ne l'explique pas.
//
// Placée ici, elle court-circuite avant. Ce qu'elle expose reste la SURFACE :
// chaque route documentée continue d'appliquer sa propre politique quand on
// l'appelle. Et elle n'est de toute façon servie qu'en Development, sauf
// `OpenApi:Enabled=true` explicite.
//
// Elle reste APRÈS la corrélation et la journalisation : une page blanche doit
// laisser une trace comme le reste.
// ═════════════════════════════════════════════════════════════════════════════
app.UseGatewayOpenApi();

app.UseAuthentication();
app.UseRateLimiter();
app.UseGatewayTokenRevocation();
app.UseAuthorization();

app.MapGatewayHealthChecks();
app.MapControllers();

// UN SEUL APPEL. NE PAS EN AJOUTER UN SECOND.
//
// `MapReverseProxy` inscrit un point de terminaison par route configurée.
// Appelé deux fois, chaque route existe en double avec le même patron et la même
// priorité : ASP.NET Core lève `AmbiguousMatchException` à la première requête
// proxifiée. Les politiques d'autorisation et de débit sont portées par la
// configuration de chaque route, pas par cet appel.
app.MapReverseProxy();

app.Run();

/// <summary>
/// Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
/// <remarks>
/// Un fichier d'instructions de haut niveau génère une classe `Program` INTERNE.
/// Sans cette déclaration, le projet de tests ne peut pas la référencer et
/// l'erreur obtenue (« inaccessible en raison de son niveau de protection »)
/// n'oriente vers aucune solution évidente.
/// </remarks>
public partial class Program
{
}
