using HBA.Food.Contracts.IntegrationEvents;
using HBA.Promotions.Infrastructure.Messaging.Kafka;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Promotions.Api.Endpoints;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Promotions.Infrastructure;
using HBA.Promotions.Infrastructure.Persistence;
using HBA.Shared.Hosting;
using HBA.Shared.IntegrationEvents;

using HBA.Promotions.Api.Grpc.Services;
using HBA.Promotions.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<PromotionsDbContext>(new PromotionsModuleInstaller());

// LES TROIS RPC DU §10.16 SONT LE CHEMIN PRINCIPAL, PAS UN EXTRA.
builder.AddHbaGrpc();

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcPromotions(builder.Configuration);

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.


// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessageriePromotions();

var app = builder.Build();

app.UseHbaService();

app.MapPromotionEndpoints();

// `MapInternalGrpcService` ET NON `MapGrpcService`.
app.MapInternalGrpcService<PromotionGrpcService>();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<PromotionsDbContext>();

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
