using HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka;
using HBA.Marketplace.ReturnRefund.Api.Endpoints;
using HBA.Marketplace.ReturnRefund.Infrastructure;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using HBA.Shared.Hosting;

using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<ReturnRefundDbContext>(new ReturnRefundModuleInstaller());
builder.AddHbaGrpc();

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
builder.Services.AjouterClientsGrpcMarketplaceReturnRefund(builder.Configuration);

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieMarketplaceReturnRefund();

var app = builder.Build();

app.UseHbaService("return-refund-service", "MARKETPLACE_RETURN_REFUND");

app.MapCustomerReturnsEndpoints();
app.MapSellerReturnsEndpoints();
app.MapAdminReturnsEndpoints();

// `MapReturnPolicyEndpoints()` A ÉTÉ RETIRÉ — IL RÉPONDAIT ET N'ÉCRIVAIT RIEN.

await app.MigrateHbaDatabaseAsync<ReturnRefundDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0.
if (app.SortirApresMigrations())
{
    return;
}

app.Run();

public partial class Program
{
}
