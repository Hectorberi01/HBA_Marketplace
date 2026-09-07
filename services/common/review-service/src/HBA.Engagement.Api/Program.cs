using HBA.Engagement.Api.Endpoints;
using HBA.Engagement.Wishlist.Infrastructure.Messaging.Kafka;
using HBA.Engagement.Reviews.Infrastructure.Messaging.Kafka;
using HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka;
using HBA.Engagement.Recommendations.Application.Recommendations;
using HBA.Engagement.Recommendations.Infrastructure.Persistence;
using HBA.Engagement.Recommendations.Infrastructure;
using HBA.Engagement.Reviews.Infrastructure.Persistence;
using HBA.Engagement.Reviews.Infrastructure;
using HBA.Engagement.Wishlist.Application.Wishlists;
using HBA.Engagement.Wishlist.Infrastructure.Persistence;
using HBA.Engagement.Wishlist.Infrastructure;
using HBA.Shared.Hosting;

using HBA.Engagement.Reviews.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<ReviewsDbContext>(new ReviewsModuleInstaller());
// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcEngagementReviews(builder.Configuration);

builder.AddHbaGrpc();

builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(UpsertRecommendationCommand).Assembly));
builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(AddToWishlistCommand).Assembly));
new RecommendationsModuleInstaller().Install(builder.Services, builder.Configuration);
new WishlistModuleInstaller().Install(builder.Services, builder.Configuration);

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieEngagementRecommendations();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieEngagementReviews();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieEngagementWishlist();

var app = builder.Build();

app.UseHbaService();

app.MapEngagementEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<RecommendationsDbContext>();
await app.MigrateHbaDatabaseAsync<ReviewsDbContext>();
await app.MigrateHbaDatabaseAsync<WishlistDbContext>();

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
