using HBA.Food.Infrastructure.Messaging.Kafka;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Api.Endpoints;
using HBA.Food.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Infrastructure;
using HBA.Food.Infrastructure.Persistence;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Hosting;
using HBA.Shared.IntegrationEvents;

using HBA.Food.Api.Grpc.Services;
using HBA.Food.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<FoodDbContext>(new FoodModuleInstaller());
builder.AddHbaGrpc();

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcFoodRestaurant(builder.Configuration);

// Le choix de l'univers, en un seul endroit.
builder.Services.AddScoped<LecteurDeCommandeALivrer>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieFoodRestaurant();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<FoodGrpcService>();
app.MapFoodEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<FoodDbContext>();

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
