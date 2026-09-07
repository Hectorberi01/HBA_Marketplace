using HBA.FoodOrders.Infrastructure.Messaging.Kafka;
using HBA.FoodOrders.Api.Endpoints;
using HBA.FoodOrders.Infrastructure;
using HBA.FoodOrders.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.FoodOrders.Api.Grpc.Services;
using HBA.FoodOrders.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<MealOrderingDbContext>(new MealOrderingModuleInstaller());

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcFoodOrder(builder.Configuration);

builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieFoodOrder();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, food-cart-service NE PEUT PAS SAVOIR SI L'ACHETEUR EN EST À SA
// PREMIÈRE COMMANDE.
app.MapInternalGrpcService<FoodOrderGrpcService>();

app.MapMealOrderEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<MealOrderingDbContext>();

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
