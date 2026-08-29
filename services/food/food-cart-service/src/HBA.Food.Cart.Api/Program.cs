using HBA.Food.Contracts.Grpc;
using HBA.FoodCarts.Api.Endpoints;
using HBA.FoodCarts.Contracts.Grpc;
using HBA.FoodCarts.Infrastructure;
using HBA.FoodCarts.Infrastructure.Persistence;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<FoodCartDbContext>(new FoodCartModuleInstaller());

// La carte du restaurant : c'est elle qui donne le prix, et non le client.
builder.Services.AddFoodGrpcClient(builder.Configuration);

// La commande de repas : « cet acheteur en est-il à sa première ? », sans quoi
// les promotions « première commande » seraient inapplicables.
builder.Services.AddFoodOrdersGrpcClient(builder.Configuration);

builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, food-order-service NE PEUT PAS LIRE LE PANIER.
//
// Le client existe de l'autre côté, la configuration pointe la bonne adresse, et
// l'appel rend « UNIMPLEMENTED ». Le symptôme apparaît au premier passage en
// commande, pas au démarrage.
app.MapInternalGrpcService<FoodCartGrpcService>();

app.MapFoodCartEndpoints();

// ═════════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<FoodCartDbContext>();

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
