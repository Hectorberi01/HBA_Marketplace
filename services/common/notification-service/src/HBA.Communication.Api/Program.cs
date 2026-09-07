using HBA.Communication.Api.Endpoints;
using HBA.Communication.Infrastructure.Messaging.Kafka;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka;
using HBA.Communication.Infrastructure;
using HBA.Communication.Infrastructure.Persistence;
using HBA.Communication.Notifications.Application.Notifications.Queries;
using HBA.Communication.Notifications.Infrastructure;
// `HBA.Products.Contracts.Grpc`, ALORS QUE LE PROJET S'APPELLE
// `HBA.Catalog.Contracts.Grpc`.
using HBA.Communication.Notifications.Infrastructure.Persistence;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Shared.Hosting;

using HBA.Communication.Notifications.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<MessagingDbContext>(new MessagingModuleInstaller());
builder.AddHbaGrpc();

// SECONDE TRANCHE DU SERVICE — GABARIT D'ENGAGEMENT-SERVICE.
builder.Services.AddMediatR(m =>
    m.RegisterServicesFromAssembly(typeof(ListMyNotificationsQuery).Assembly));

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcCommunicationNotifications(builder.Configuration);

builder.Services.AddScoped<AcheteurDuTicket>();

new NotificationsModuleInstaller().Install(builder.Services, builder.Configuration);

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieCommunicationNotifications();

// La messagerie interne a son propre module : son outbox y est cablee.
builder.Services.AjouterMessagerieCommunication();

var app = builder.Build();

app.UseHbaService();

app.MapCommunicationEndpoints();
app.MapNotificationsEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<MessagingDbContext>();

// Deux tranches, deux schémas : la sonde /health/ready ne surveille que le premier,
// et le service se déclarerait apte sans la moindre table de notification.
await app.MigrateHbaDatabaseAsync<NotificationsDbContext>();

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
