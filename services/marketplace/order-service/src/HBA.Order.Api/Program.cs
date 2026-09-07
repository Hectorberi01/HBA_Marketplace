using HBA.Orders.Infrastructure.Messaging.Kafka;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Api.Endpoints;
using HBA.Orders.Infrastructure;
using HBA.Orders.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Orders.Api.Grpc.Services;
using HBA.Orders.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<OrderingDbContext>(new OrderingModuleInstaller());
// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcOrder(builder.Configuration);

// ENREGISTRÉ AUSSI SOUS SON TYPE CONCRET.
builder.Services.AddScoped<CreateDeliveryOnOrderConfirmedHandler>();

builder.Services.AddScoped<
    IIntegrationEventHandler<OrderConfirmedIntegrationEvent>>(
    sp => sp.GetRequiredService<CreateDeliveryOnOrderConfirmedHandler>());

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.


builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieOrder();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<OrderingGrpcService>();
app.MapOrderEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<OrderingDbContext>();

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
