using HBA.Financial.Api.Endpoints;
using HBA.Financial.Api.GrpcServices;
using HBA.Financial.Billing.Application.Commissions;
using HBA.Financial.Billing.Infrastructure.Persistence;
using HBA.Financial.Billing.Infrastructure;
using HBA.Financial.Payments.Infrastructure.Persistence;
using HBA.Financial.Payments.Infrastructure;
using HBA.Financial.Wallet.Application.Batches;
using HBA.Financial.Wallet.Infrastructure.Persistence;
using HBA.Financial.Wallet.Infrastructure;
using HBA.Deliveries.Contracts.Grpc;
using HBA.Food.Contracts.Grpc;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<PaymentsDbContext>(new PaymentsModuleInstaller());
builder.Services.AddOrderingGrpcClient(builder.Configuration);
builder.Services.AddFoodGrpcClient(builder.Configuration);

// Commandes de repas : lues par `IPayableOrderReader` pour ouvrir leur paiement
// (lot 6.1). À ne pas confondre avec la ligne au-dessus, qui vise
// restaurant-service.
builder.Services.AddFoodOrdersGrpcClient(builder.Configuration);
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

// CE CLIENT NE SERT QU'À UNE AUTORISATION.
//
// `GET /api/financial/wallets/drivers/{driverId}` et son relevé doivent
// vérifier que l'appelant EST ce livreur. Le jeton porte un identifiant
// d'utilisateur, la route un identifiant de livreur ; seule delivery-service
// connaît la correspondance (`GetDriverAccountAsync`). Faute de ce client, les
// deux routes avaient été rangées chez l'admin — et l'écran « Gains » du BFF
// livreur rendait 403 à tous les livreurs.
//
// Aucun flux métier ne passe par là : financial-service ne crée ni ne pilote
// de course.
builder.Services.AddDeliveryGrpcClient(builder.Configuration);

builder.AddHbaGrpc();

builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(CreateCommissionRuleCommand).Assembly));
builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(RunSettlementCommand).Assembly));
new BillingModuleInstaller().Install(builder.Services, builder.Configuration);
new WalletModuleInstaller().Install(builder.Services, builder.Configuration);

var app = builder.Build();

app.UseHbaService();

app.MapFinancialEndpoints();
app.MapInternalGrpcService<FinancialGrpcService>();

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
await app.MigrateHbaDatabaseAsync<BillingDbContext>();
await app.MigrateHbaDatabaseAsync<PaymentsDbContext>();
await app.MigrateHbaDatabaseAsync<WalletDbContext>();

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
