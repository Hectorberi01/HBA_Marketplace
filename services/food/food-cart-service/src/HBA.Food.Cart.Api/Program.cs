using HBA.FoodCarts.Infrastructure.Messaging.Kafka;
using HBA.FoodCarts.Api.Endpoints;
using HBA.FoodCarts.Infrastructure;
using HBA.FoodCarts.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.FoodCarts.Api.Grpc.Services;
using HBA.FoodCarts.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<FoodCartDbContext>(new FoodCartModuleInstaller());

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcFoodCart(builder.Configuration);

builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieFoodCart();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, food-order-service NE PEUT PAS LIRE LE PANIER.
app.MapInternalGrpcService<FoodCartGrpcService>();

app.MapFoodCartEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<FoodCartDbContext>();

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
