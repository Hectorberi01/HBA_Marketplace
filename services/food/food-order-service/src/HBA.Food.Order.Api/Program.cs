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
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcFoodOrder(builder.Configuration);

builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieFoodOrder();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, food-cart-service NE PEUT PAS SAVOIR SI L'ACHETEUR EN EST
// À SA PREMIÈRE COMMANDE.
//
// Le client existe de l'autre côté, la configuration pointe la bonne adresse, et
// l'appel rend « UNIMPLEMENTED ». Le symptôme apparaît à la première lecture de
// panier, pas au démarrage.
app.MapInternalGrpcService<FoodOrderGrpcService>();

app.MapMealOrderEndpoints();

// ═════════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<MealOrderingDbContext>();

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
