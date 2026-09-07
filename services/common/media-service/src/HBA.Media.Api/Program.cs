using HBA.Media.Api.Endpoints;
using HBA.Media.Infrastructure.Messaging.Kafka;
using HBA.Media.Infrastructure;
using HBA.Media.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Media.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<MediaDbContext>(new MediaModuleInstaller());
builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieMedia();

var app = builder.Build();

app.UseHbaService();

// REST/JSON sur 8080 — le trafic client, via la passerelle.
app.MapMediaEndpoints();

// gRPC sur 8081 — le trafic entre services.
app.MapInternalGrpcService<MediaGrpcService>();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<MediaDbContext>();

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
