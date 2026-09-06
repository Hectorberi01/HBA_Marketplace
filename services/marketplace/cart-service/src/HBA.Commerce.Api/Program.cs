using HBA.Commerce.Api.Endpoints;
using HBA.Commerce.Infrastructure.Messaging.Kafka;
using HBA.Commerce.Infrastructure;
using HBA.Commerce.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Commerce.Api.Grpc.Services;
using HBA.Commerce.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<CartDbContext>(new CartModuleInstaller());
// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcCommerce(builder.Configuration);

builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieCommerce();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, order-service NE PEUT PAS LIRE LE PANIER.
//
// Le client gRPC existe de l'autre côté, la configuration pointe la bonne
// adresse, et l'appel rend `UNIMPLEMENTED` : rien dans commerce-service ne
// répond sur `hba.commerce.v1.CommerceApi`. Le symptôme apparaît au premier
// checkout, pas au démarrage.
app.MapInternalGrpcService<CommerceGrpcService>();

app.MapCommerceEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<CartDbContext>();

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
