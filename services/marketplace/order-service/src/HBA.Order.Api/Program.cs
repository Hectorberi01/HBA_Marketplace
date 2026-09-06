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
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcOrder(builder.Configuration);

// ENREGISTRÉ AUSSI SOUS SON TYPE CONCRET.
//
// La route de relance d'arbitrage (`POST /api/admin/orders/{id}/review/resume`)
// rejoue l'étape « créer la course d'une commande confirmée » en appelant
// `DemanderCourseAsync`. Sans cette ligne, seul le contrat d'événement serait
// résoluble et il faudrait fabriquer un faux `OrderConfirmed` pour y entrer —
// c'est-à-dire faire croire qu'une confirmation a eu lieu.
builder.Services.AddScoped<CreateDeliveryOnOrderConfirmedHandler>();

builder.Services.AddScoped<
    IIntegrationEventHandler<OrderConfirmedIntegrationEvent>>(
    sp => sp.GetRequiredService<CreateDeliveryOnOrderConfirmedHandler>());

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.


builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieOrder();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<OrderingGrpcService>();
app.MapOrderEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<OrderingDbContext>();

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
