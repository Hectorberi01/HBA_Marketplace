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
builder.Services.AjouterClientsGrpcMerchants(builder.Configuration);

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieMerchants();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<MerchantsGrpcService>();
app.MapMerchantEndpoints();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<SellersDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0.
if (app.SortirApresMigrations())
{
    return;
}

// LES RÔLES SYSTÈME, APRÈS LA MIGRATION ET AVANT D'OUVRIR LE PORT.
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
    // l'existence.
    app.Logger.LogInformation(
        "Rôles vendeur système non amorcés au démarrage : Database:MigrateOnStartup vaut false. "
        + "Ils doivent être semés en même temps que les migrations.");
}

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
