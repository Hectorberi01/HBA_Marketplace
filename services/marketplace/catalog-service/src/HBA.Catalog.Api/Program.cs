using HBA.Catalog.Api.Endpoints;
using HBA.Catalog.Infrastructure.Messaging.Kafka;
using HBA.Catalog.Infrastructure;
using HBA.Catalog.Infrastructure.Persistence;
using HBA.Media.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Products.Contracts.Grpc;
using HBA.Shared.Hosting;

using HBA.Catalog.Api.Grpc.Services;
using HBA.Catalog.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<CatalogDbContext>(new CatalogModuleInstaller());

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcCatalog(builder.Configuration);

builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieCatalog();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<CatalogGrpcService>();
app.MapCatalogEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<CatalogDbContext>();

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
