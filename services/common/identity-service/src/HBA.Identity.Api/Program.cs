using HBA.Identity.Api;
using HBA.Identity.Infrastructure.Messaging.Kafka;
using HBA.Identity.Api.Endpoints;
using HBA.Identity.Infrastructure;
using HBA.Identity.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Identity.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<IdentityDbContext>(new IdentityModuleInstaller());
builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieIdentity();

var app = builder.Build();

app.UseHbaService();

// CE SERVICE SERT `/api/v1/auth/*` — ET CE COMMENTAIRE A DIT LE CONTRAIRE PENDANT
// TOUTE LA DURÉE DE LA PANNE (ISSUE-063).
app.MapIdentityEndpoints();

app.MapInternalGrpcService<IdentityGrpcService>();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<IdentityDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0.
if (app.SortirApresMigrations())
{
    return;
}

// APRÈS LES MIGRATIONS, ET C'EST UN ORDRE, PAS UNE PRÉFÉRENCE.
await app.SeedIdentityAsync();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
