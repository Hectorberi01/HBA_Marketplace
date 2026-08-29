using HBA.Inventory.Api.Endpoints;
using HBA.Inventory.Contracts.Grpc;
using HBA.Inventory.Infrastructure;
using HBA.Inventory.Infrastructure.Persistence;
using HBA.Merchants.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<InventoryDbContext>(new InventoryModuleInstaller());
builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// SANS CE CLIENT, LES ÉCRITURES DE STOCK RESTENT FERMÉES AU VENDEUR.
//
// inventory-service ne connaît pas la correspondance compte → vendeur : elle
// appartient à merchant-service. C'est précisément ce qui l'avait obligé à
// ranger ses douze écritures sous `MapAdminGroup`, faute de pouvoir vérifier
// qu'un appelant est bien le propriétaire du lieu qu'il modifie.
//
// EXIGE `SERVICES__MERCHANT` — déjà posé pour ce service dans
// `docker-compose.dev.yml`. `AddMerchantsGrpcClient` remplace de toute façon le
// port de l'URL par `Hosting:GrpcPort` : le `:9090` qu'on y lit est décoratif.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<InventoryGrpcService>();
app.MapInventoryEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<InventoryDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0. Placé APRÈS le dernier
// `MigrateHbaDatabaseAsync` — plusieurs services portent plusieurs DbContext, et
// sortir après le premier laisserait les autres bases sans schéma.
if (app.SortirApresMigrations())
{
    return;
}

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
