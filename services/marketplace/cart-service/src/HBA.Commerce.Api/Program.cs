using HBA.Commerce.Api.Endpoints;
using HBA.Commerce.Contracts.Grpc;
using HBA.Commerce.Infrastructure;
using HBA.Commerce.Infrastructure.Persistence;
using HBA.Inventory.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Products.Contracts.Grpc;
using HBA.Promotions.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<CartDbContext>(new CartModuleInstaller());
builder.Services.AddProductsGrpcClient(builder.Configuration);
builder.Services.AddInventoryGrpcClient(builder.Configuration);
builder.Services.AddOrderingGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════════
// SANS CETTE LIGNE, AUCUNE CAMPAGNE COMMERCIALE N'EXISTE (ISSUE-033).
//
// La tarification neutre — qui fut le seul fournisseur du dépôt —
// rendait des remises nulles et refusait tout coupon. promotion-service, complet
// depuis son écriture, n'avait AUCUN appelant. C'est ce client qui lui en donne
// un.
//
// `AddPromotionGrpcClient` LÈVE à la construction de l'hôte si `Services:Promotion`
// est absent, et c'est le bon sens de l'erreur : un panier démarré sans savoir
// joindre promotion refuserait silencieusement tous les coupons, et les clients
// paieraient le plein tarif sans que rien ne le signale. Mieux vaut ne pas
// démarrer.
//
// À NE PAS CONFONDRE AVEC LE REPLI D'EXÉCUTION.
//
// Une adresse ABSENTE est une erreur de déploiement : elle se corrige, et elle se
// corrige mieux avant d'ouvrir le port. Un service INJOIGNABLE en cours de route
// est un incident : là, `PromotionPricingModuleApi` valorise le panier sans remise
// et le journalise, parce qu'une panne de promotion ne doit pas devenir une panne
// de vente.
// ═════════════════════════════════════════════════════════════════════════════
builder.Services.AddPromotionGrpcClient(builder.Configuration);

builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, order-service NE PEUT PAS LIRE LE PANIER.
//
// Le client gRPC existe de l'autre côté, la configuration pointe la bonne
// adresse, et l'appel rend `UNIMPLEMENTED` : rien dans commerce-service ne
// répond sur `hba.commerce.v1.CommerceApi`. Le symptôme apparaît au premier
// checkout, pas au démarrage.
app.MapInternalGrpcService<CommerceGrpcService>();

app.MapCommerceEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<CartDbContext>();

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
