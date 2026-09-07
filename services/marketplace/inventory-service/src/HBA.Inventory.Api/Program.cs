using HBA.Inventory.Api.Endpoints;
using HBA.Inventory.Infrastructure.Messaging.Kafka;
using HBA.Inventory.Infrastructure;
using HBA.Inventory.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Inventory.Api.Grpc.Services;
using HBA.Inventory.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<InventoryDbContext>(new InventoryModuleInstaller());
builder.AddHbaGrpc();

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcInventory(builder.Configuration);

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieInventory();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<InventoryGrpcService>();
app.MapInventoryEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<InventoryDbContext>();

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
