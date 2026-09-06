using HBA.Communication.Api.Endpoints;
using HBA.Communication.Infrastructure.Messaging.Kafka;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka;
using HBA.Communication.Infrastructure;
using HBA.Communication.Infrastructure.Persistence;
using HBA.Communication.Notifications.Application.Notifications.Queries;
using HBA.Communication.Notifications.Infrastructure;
using HBA.Identity.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;

// `HBA.Products.Contracts.Grpc`, ALORS QUE LE PROJET S'APPELLE
//    `HBA.Catalog.Contracts.Grpc`.
//
// Le fichier `ProductsGrpc.cs` vit dans le projet Catalog mais déclare l'espace
// de noms Products. La référence projet est donc bien celle de Catalog, et le
// `using` celui de Products — les deux ne se déduisent pas l'un de l'autre.
//
// C'est la trace de la dualité Catalog/Products : Products est le successeur,
// son client gRPC est encore hébergé par le projet de son prédécesseur.
using HBA.Products.Contracts.Grpc;
using HBA.Deliveries.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Communication.Notifications.Infrastructure.Persistence;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Shared.Hosting;

using HBA.Communication.Notifications.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<MessagingDbContext>(new MessagingModuleInstaller());
builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// SECONDE TRANCHE DU SERVICE — GABARIT D'ENGAGEMENT-SERVICE.
//
// `AddHbaService` n'installe qu'UN module : celui dont le DbContext est sondé
// par /health/ready. Les tranches suivantes s'installent à la main, et leur
// assembly doit être scannée explicitement — `AddMediatR` ne scanne QUE
// l'assembly qu'on lui nomme, et une notification dont le gestionnaire n'est
// pas enregistré ne produit aucune erreur : elle n'est simplement jamais
// traitée.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddMediatR(m =>
    m.RegisterServicesFromAssembly(typeof(ListMyNotificationsQuery).Assembly));

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcCommunicationNotifications(builder.Configuration);

builder.Services.AddScoped<AcheteurDuTicket>();

new NotificationsModuleInstaller().Install(builder.Services, builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieCommunicationNotifications();

// La messagerie interne a son propre module : son outbox y est cablee.
builder.Services.AjouterMessagerieCommunication();

var app = builder.Build();

app.UseHbaService();

app.MapCommunicationEndpoints();
app.MapNotificationsEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<MessagingDbContext>();

// Deux tranches, deux schémas : la sonde /health/ready ne surveille que le
// premier, et le service se déclarerait apte sans la moindre table de
// notification.
await app.MigrateHbaDatabaseAsync<NotificationsDbContext>();

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
