using HBA.Identity.Contracts.Grpc;
using HBA.Inventory.Contracts.Grpc;
using HBA.Media.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Merchants.Api.Endpoints;
using HBA.Merchants.Contracts.Grpc;
using HBA.Merchants.Infrastructure;
using HBA.Merchants.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<SellersDbContext>(new SellersModuleInstaller());
builder.AddHbaGrpc();
builder.Services.AddIdentityGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// SANS CETTE LIGNE, N'IMPORTE QUEL MÉDIA DE LA PLATEFORME POUVAIT DEVENIR
//    UNE PIÈCE KYB.
//
// `AddKybDocumentCommandHandler` vérifie désormais que le fichier appartient à
// CE vendeur et qu'il est bien une pièce légale. Il lui faut donc `IMediaModuleApi`.
//
// Le service n'avait aucun client média : le domaine renvoyait le contrôle « à la
// couche qui voit les deux », la documentation renvoyait ensuite au BFF Vendeur —
// qui est un squelette sans aucun cas d'usage. La délégation ne pointait vers
// personne, et un vendeur rattachait à son dossier la pièce d'identité d'un
// concurrent avant de s'en faire signer l'URL.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddMediaGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// ET SANS CELLE-CI, UNE BOUTIQUE EXPÉDIAIT DEPUIS L'ADRESSE D'UN CONCURRENT.
//
// `AttachStoreLocationCommand` acceptait n'importe quel GUID. L'identifiant
// partait ensuite vers delivery, qui bâtissait un enlèvement coursier sur une
// adresse que le vendeur ne contrôle pas — et un GUID inexistant ne se
// manifestait qu'APRÈS le paiement de l'acheteur.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddInventoryGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// POUR RECALCULER LE COMPTEUR DE VENTES, PAS POUR L'INCRÉMENTER.
//
// `SellerSalesCountHandler` redemande le total à order-service à chaque commande
// confirmée : poser une valeur exacte est idempotent, incrémenter double-compte
// au premier rejeu — et Kafka livre au moins une fois.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddOrderingGrpcClient(builder.Configuration);

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
