using HBA.Deliveries.Api.Endpoints;
using HBA.Deliveries.Api.Grpc;
using HBA.Deliveries.Infrastructure;
using HBA.Deliveries.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<DeliveriesDbContext>(new DeliveriesModuleInstaller());
builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, order-service ET food-service NE PEUVENT PAS CRÉER DE
//    COURSE — et l'appel rend UNIMPLEMENTED au premier repas prêt, pas au
//    démarrage.
app.MapInternalGrpcService<DeliveryGrpcService>();

app.MapDeliveryEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SANS CETTE LIGNE, AUCUNE COURSE N'EST JAMAIS PROPOSÉE À PERSONNE.
//
// C'est ici que vit `POST /api/deliveries/mine/position`, seul appelant de
// `IDriverLocationCache.SetAsync` de toute la plateforme. Tant qu'il n'existait
// pas, `DispatchDeliveryCommandHandler` interrogeait un cache que rien
// n'alimentait, concluait « aucun livreur disponible » et abandonnait la course
// après cinq tentatives — c'est la rupture que nomme la décision D30.
//
// Le même groupe porte l'acceptation d'une proposition et les cinq étapes
// d'exécution, toutes décrites dans le code depuis des mois et mappées nulle
// part (lot 5.2, ISSUE-029 / ISSUE-030).
// ═════════════════════════════════════════════════════════════════════════
app.MapDriverDeliveryEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<DeliveriesDbContext>();

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
