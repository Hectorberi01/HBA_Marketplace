using HBA.Users.Api.Endpoints;
using HBA.Users.Infrastructure.Messaging.Kafka;
using HBA.Shared.Hosting;
using HBA.Users.Infrastructure;
using HBA.Users.Infrastructure.Persistence;

using HBA.Users.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<UsersDbContext>(new UsersModuleInstaller());
builder.AddHbaGrpc();

// LE CLIENT gRPC VERS identity-service A ÉTÉ RETIRÉ.

// TOUT CE QUE CE SERVICE ÉCOUTE EST DÉCLARÉ DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieUsers();

var app = builder.Build();

app.UseHbaService();

app.MapUserEndpoints();

app.MapInternalGrpcService<UsersGrpcService>();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<UsersDbContext>();

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
