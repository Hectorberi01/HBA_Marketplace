using HBA.Deliveries.Api.Endpoints;
using HBA.Deliveries.Infrastructure.Messaging.Kafka;
using HBA.Deliveries.Infrastructure;
using HBA.Deliveries.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Deliveries.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<DeliveriesDbContext>(new DeliveriesModuleInstaller());
builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieDeliveryCore();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, order-service ET food-service NE PEUVENT PAS CRÉER DE COURSE —
// et l'appel rend UNIMPLEMENTED au premier repas prêt, pas au démarrage.
app.MapInternalGrpcService<DeliveryGrpcService>();

app.MapDeliveryEndpoints();

// SANS CETTE LIGNE, AUCUNE COURSE N'EST JAMAIS PROPOSÉE À PERSONNE.
app.MapDriverDeliveryEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<DeliveriesDbContext>();

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
