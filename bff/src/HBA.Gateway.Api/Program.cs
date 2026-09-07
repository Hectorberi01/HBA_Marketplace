using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Infrastructure.Messaging.Kafka;
using HBA.Gateway.Api.Middlewares;
using HBA.Gateway.Application;
using HBA.Gateway.Application.Abstractions;
using HBA.Gateway.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
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

// LA PASSERELLE CONSOMME UN EVENEMENT, ET UN SEUL.
builder.Services.AjouterMessagerieGateway(builder.Configuration);

// Porte l'identifiant de corrélation jusqu'à la couche Application sans lui exposer
// HttpContext.
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

// PIPELINE — L'ORDRE EST UNE DÉCISION, PAS UNE MISE EN FORME.
app.UseForwardedHeaders();

app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// LA DOCUMENTATION PASSE AVANT L'AUTORISATION, ET CE N'EST PAS UN CONFORT.
app.UseGatewayOpenApi();

app.UseAuthentication();
app.UseRateLimiter();
app.UseGatewayTokenRevocation();
app.UseAuthorization();

app.MapGatewayHealthChecks();
app.MapControllers();

// UN SEUL APPEL. NE PAS EN AJOUTER UN SECOND.
app.MapReverseProxy();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program { }
