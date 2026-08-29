using HBA.Engagement.Api.Endpoints;
using HBA.Engagement.Recommendations.Application.Recommendations;
using HBA.Engagement.Recommendations.Infrastructure.Persistence;
using HBA.Engagement.Recommendations.Infrastructure;
using HBA.Engagement.Reviews.Infrastructure.Persistence;
using HBA.Engagement.Reviews.Infrastructure;
using HBA.Engagement.Wishlist.Application.Wishlists;
using HBA.Engagement.Wishlist.Infrastructure.Persistence;
using HBA.Engagement.Wishlist.Infrastructure;
using HBA.Merchants.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<ReviewsDbContext>(new ReviewsModuleInstaller());
builder.Services.AddOrderingGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════════
// LA RÉSOLUTION VENDEUR — SANS ELLE, LA RÉPONSE AUX AVIS RESTE OUVERTE À TOUS.
//
// `AddMerchantsGrpcClient` LÈVE SI `Services:Merchant` EST ABSENT.
//
// La levée se produit à la CONSTRUCTION de l'hôte, donc le conteneur ne démarre
// pas du tout. C'est exactement ce qui était arrivé à ce service avec
// `Services:Order` — voir le commentaire de `compose.services.yml`. La clé est
// donc posée dans le même bloc, et `AuthorizationTestFactory` la fournit déjà.
// ═════════════════════════════════════════════════════════════════════════════
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

builder.AddHbaGrpc();

builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(UpsertRecommendationCommand).Assembly));
builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(AddToWishlistCommand).Assembly));
new RecommendationsModuleInstaller().Install(builder.Services, builder.Configuration);
new WishlistModuleInstaller().Install(builder.Services, builder.Configuration);

var app = builder.Build();

app.UseHbaService();

app.MapEngagementEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
//
// TROIS DbContext, DONC TROIS APPELS.
//
// N'en migrer qu'un laisse les autres sans tables. Et la sonde
// /health/ready ne surveille que le premier : le service se
// déclarerait apte avec les deux tiers de son schéma absents.
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<RecommendationsDbContext>();
await app.MigrateHbaDatabaseAsync<ReviewsDbContext>();
await app.MigrateHbaDatabaseAsync<WishlistDbContext>();

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
