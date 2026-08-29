using HBA.Communication.Api.Endpoints;
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
using HBA.Shared.Hosting;

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

// SANS CES TROIS LIGNES, LE MODULE DÉMARRE ET ÉCHOUE À LA PREMIÈRE
//    NOTIFICATION — PAS AU DÉMARRAGE.
//
// Trois gestionnaires ont besoin de remonter d'un identifiant à un destinataire :
// un avis porte un produit, une rupture porte un SKU, une inscription vendeur
// n'indique aucun administrateur. Les interfaces sont résolues par le conteneur
// à la CONSTRUCTION du gestionnaire, c'est-à-dire à la réception de l'événement.
// Une dépendance manquante ne se voit donc pas au démarrage : elle se voit quand
// une notification n'arrive pas.
//
// `AddProductsGrpcClient` pointe vers `Services:Catalog` — c'est catalog-service
// qui héberge Products.
builder.Services.AddProductsGrpcClient(builder.Configuration);
builder.Services.AddMerchantsGrpcClient(builder.Configuration);
builder.Services.AddIdentityGrpcClient(builder.Configuration);

// CELUI-CI MANQUAIT, ET C'EST LE CONTENEUR QUI L'A DIT — AU DÉMARRAGE.
//
// Quatre gestionnaires réclament `IOrderingModuleApi` : le paiement refusé, les
// deux étapes d'expédition, le cycle de vie vendeur. Tous doivent remonter d'un
// identifiant de commande à son acheteur pour savoir À QUI écrire.
//
// Le commentaire ci-dessus annonçait qu'une dépendance manquante ne se verrait
// qu'à la réception d'un événement. C'était vrai des gestionnaires construits à
// la demande — mais `ValidateOnBuild` valide TOUS les descripteurs enregistrés
// dès la construction du conteneur. Le service ne démarrait plus du tout.
//
// La validation au démarrage vaut mieux : une notification qui n'arrive pas ne
// se remarque que le jour où quelqu'un s'en plaint.
builder.Services.AddOrderingGrpcClient(builder.Configuration);

// LE SECOND UNIVERS DE COMMANDES. Les quatre notifications de cuisine
// résolvaient l'acheteur chez order-service seulement : le client d'une commande
// de repas ne recevait donc AUCUN suivi, et l'échec ne produisait qu'un Warning.
builder.Services.AddFoodOrdersGrpcClient(builder.Configuration);
builder.Services.AddScoped<AcheteurDuTicket>();

// POUR PRÉVENIR LE LIVREUR, IL FAUT REMONTER À SON COMPTE.
//
// `DeliveryAssignedIntegrationEvent` ne porte que le `DriverId` : il part aussi
// vers l'API partenaires, à qui le compte HBA d'un livreur ne regarde pas. La
// conversion se fait donc par lecture, pas par élargissement de l'événement.
builder.Services.AddDeliveryGrpcClient(builder.Configuration);

new NotificationsModuleInstaller().Install(builder.Services, builder.Configuration);

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
