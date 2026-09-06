using HBA.Merchants.Infrastructure.Messaging.Kafka;
using HBA.Merchants.Api.Endpoints;
using HBA.Merchants.Infrastructure;
using HBA.Merchants.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Merchants.Api.Grpc.Services;
using HBA.Merchants.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<SellersDbContext>(new SellersModuleInstaller());
builder.AddHbaGrpc();
// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcMerchants(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieMerchants();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<MerchantsGrpcService>();
app.MapMerchantEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<SellersDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0. Placé APRÈS le dernier
// `MigrateHbaDatabaseAsync` — plusieurs services portent plusieurs DbContext, et
// sortir après le premier laisserait les autres bases sans schéma.
if (app.SortirApresMigrations())
{
    return;
}

// ═════════════════════════════════════════════════════════════════════════
// LES RÔLES SYSTÈME, APRÈS LA MIGRATION ET AVANT D'OUVRIR LE PORT.
//
// MÊME MOTIF QUE `SeedIdentityAsync`, ET MÊME RAISON D'ÊTRE DU CÔTÉ DU CODE.
//
// Les permissions par défaut d'un rôle sont du CODE — la liste de
// `SystemSellerRoles`. Les figer dans un `InsertData` de migration ferait
// diverger les bases neuves des anciennes dès la première correction de droits.
// L'amorçage est idempotent : il crée ce qui manque, recale les permissions de
// ce qui existe, et ne touche jamais un rôle personnalisé.
//
// CONDITIONNÉ AU MÊME RÉGLAGE QUE LA MIGRATION, ET CE N'EST PAS ANODIN.
//
// `AuthorizationTestFactory` démarre cet hôte avec `Database:MigrateOnStartup`
// à faux et une chaîne de connexion vers un port fermé : les tables n'existent
// pas. Un amorçage inconditionnel ferait échouer le DÉMARRAGE, donc les cinq
// tests d'autorisation de ce service — et l'erreur désignerait le semis alors
// que la cause serait ce couplage. Là où les migrations sont appliquées hors
// ligne, l'amorçage doit l'être aussi.
// ═════════════════════════════════════════════════════════════════════════
var migreAuDemarrage = app.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>()?.MigrateOnStartup ?? app.Environment.IsDevelopment();

if (migreAuDemarrage)
{
    await using var scope = app.Services.CreateAsyncScope();
    var sellersDb = scope.ServiceProvider.GetRequiredService<SellersDbContext>();

    await MerchantsDataSeeder.SeedSystemRolesAsync(sellersDb);
    app.Logger.LogInformation("Rôles vendeur système vérifiés.");
}
else
{
    // Journalisé, et non passé sous silence : « aucun rôle attribuable » est un
    // symptôme qu'on met longtemps à relier à un réglage dont on ignorait
    // l'existence. Même raisonnement que le message de la migration.
    app.Logger.LogInformation(
        "Rôles vendeur système non amorcés au démarrage : Database:MigrateOnStartup vaut false. "
        + "Ils doivent être semés en même temps que les migrations.");
}

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
