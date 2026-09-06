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
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcFinancialPayments(builder.Configuration);

builder.AddHbaGrpc();

builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(CreateCommissionRuleCommand).Assembly));
builder.Services.AddMediatR(m => m.RegisterServicesFromAssembly(typeof(RunSettlementCommand).Assembly));
new BillingModuleInstaller().Install(builder.Services, builder.Configuration);
new WalletModuleInstaller().Install(builder.Services, builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieFinancialBilling();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieFinancialPayments();

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieFinancialWallet();

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
