using HBA.Analytics.Api.Endpoints;
using HBA.Analytics.Infrastructure;
using HBA.Analytics.Infrastructure.Grpc;
using HBA.Analytics.Infrastructure.Messaging.Kafka;
using HBA.Analytics.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<AnalyticsDbContext>(new AnalyticsModuleInstaller());

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcAnalytics(builder.Configuration);

builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieAnalytics();

var app = builder.Build();

app.UseHbaService();

app.MapAnalyticsEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<AnalyticsDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0.
if (app.SortirApresMigrations())
{
    return;
}

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
