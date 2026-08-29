using HBA.Media.Api.Endpoints;
using HBA.Media.Contracts.Grpc;
using HBA.Media.Infrastructure;
using HBA.Media.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<MediaDbContext>(new MediaModuleInstaller());
builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

// REST/JSON sur 8080 — le trafic client, via la passerelle.
app.MapMediaEndpoints();

// gRPC sur 8081 — le trafic entre services. Ce port n'est routé par AUCUNE
// route YARP : la seule façon de l'atteindre est d'être sur `hba-backend`.
app.MapInternalGrpcService<MediaGrpcService>();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<MediaDbContext>();

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
