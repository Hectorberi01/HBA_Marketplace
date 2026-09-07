using HBA.Financial.Api.Endpoints;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka;
using HBA.Financial.Billing.Infrastructure.Messaging.Kafka;
using HBA.Financial.Billing.Application.Commissions;
using HBA.Financial.Billing.Infrastructure.Persistence;
using HBA.Financial.Billing.Infrastructure;
using HBA.Financial.Payments.Infrastructure.Persistence;
using HBA.Financial.Payments.Infrastructure;
using HBA.Financial.Wallet.Application.Batches;
using HBA.Financial.Wallet.Infrastructure.Persistence;
using HBA.Financial.Wallet.Infrastructure;
using HBA.Shared.Hosting;

using HBA.Financial.Api.Grpc.Services;
using HBA.Financial.Payments.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<PaymentsDbContext>(new PaymentsModuleInstaller());
// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcFinancialPayments(builder.Configuration);

builder.AddHbaGrpc();

builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(CreateCommissionRuleCommand).Assembly));
builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(RunSettlementCommand).Assembly));
new BillingModuleInstaller().Install(builder.Services, builder.Configuration);
new WalletModuleInstaller().Install(builder.Services, builder.Configuration);

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieFinancialBilling();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieFinancialPayments();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieFinancialWallet();

var app = builder.Build();

app.UseHbaService();

app.MapFinancialEndpoints();
app.MapInternalGrpcService<FinancialGrpcService>();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<BillingDbContext>();
await app.MigrateHbaDatabaseAsync<PaymentsDbContext>();
await app.MigrateHbaDatabaseAsync<WalletDbContext>();

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
