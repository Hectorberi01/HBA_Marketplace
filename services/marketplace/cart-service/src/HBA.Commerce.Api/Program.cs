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
builder.Services.AjouterClientsGrpcCommerce(builder.Configuration);

builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieCommerce();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, order-service NE PEUT PAS LIRE LE PANIER.
app.MapInternalGrpcService<CommerceGrpcService>();

app.MapCommerceEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<CartDbContext>();

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
