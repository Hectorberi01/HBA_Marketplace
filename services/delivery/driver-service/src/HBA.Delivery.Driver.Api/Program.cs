using HBA.Drivers.Api.Endpoints;
using HBA.Drivers.Infrastructure.Messaging.Kafka;
using HBA.Drivers.Infrastructure;
using HBA.Drivers.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Drivers.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

// CE SERVICE PASSE DE `AddHbaSecurity` À `AddHbaService<TDbContext>`.
builder.AddHbaService<DriverDbContext>(new DriversModuleInstaller());
builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieDeliveryDriver();

var app = builder.Build();

app.UseHbaService("driver-service", "DELIVERY_DRIVER");

app.MapDriverEndpoints();
app.MapInternalGrpcService<DriversGrpcService>();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<DriverDbContext>();

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
