using HBA.Analytics.Api.Endpoints;
using HBA.Analytics.Infrastructure;
using HBA.Analytics.Infrastructure.Grpc;
using HBA.Analytics.Infrastructure.Messaging.Kafka;
using HBA.Analytics.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<AnalyticsDbContext>(new AnalyticsModuleInstaller());

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
//
// Un seul : le client marchand, qui repond a « ce compte, quel vendeur
// est-il ? ». La raison complete est dans
// `Infrastructure/Grpc/DependencyInjection.cs` — la separer d'ici produirait
// deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcAnalytics(builder.Configuration);

builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'inbox : l'oublier laisserait un service qui demarre,
// sert ses routes, et ne consomme plus rien — les graphes se figeraient sur les
// chiffres de la veille sans qu'aucune erreur ne le dise. `GardeDeCablage`,
// enregistree par l'installeur, refuse le demarrage dans ce cas.
//
// CE SERVICE NE PUBLIE RIEN : il n'y a donc pas d'outbox a drainer ici, et
// c'est la seule difference avec les vingt-cinq autres `Program.cs`. Voir
// `Persistence/Outbox/LISEZMOI.md`.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieAnalytics();

var app = builder.Build();

app.UseHbaService();

app.MapAnalyticsEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<AnalyticsDbContext>();

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
