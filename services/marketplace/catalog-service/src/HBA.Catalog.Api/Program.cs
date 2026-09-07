using HBA.Catalog.Api.Endpoints;
using HBA.Catalog.Infrastructure.Messaging.Kafka;
using HBA.Catalog.Infrastructure;
using HBA.Catalog.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Catalog.Api.Grpc.Services;
using HBA.Catalog.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<CatalogDbContext>(new CatalogModuleInstaller());

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcCatalog(builder.Configuration);

builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieCatalog();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<CatalogGrpcService>();
app.MapCatalogEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<CatalogDbContext>();

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
