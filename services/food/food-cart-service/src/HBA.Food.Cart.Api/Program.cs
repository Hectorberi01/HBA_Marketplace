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
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcFoodCart(builder.Configuration);

builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieFoodCart();

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
