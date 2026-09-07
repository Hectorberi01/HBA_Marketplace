using HBA.Delivery.Pricing.Api.Endpoints;
using HBA.Delivery.Pricing.Infrastructure.Messaging.Kafka;
using HBA.Delivery.Pricing.Infrastructure;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using HBA.Shared.Hosting;
using HBA.Shared.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using HBA.Delivery.Pricing.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

// CE SERVICE N'AVAIT AUCUNE AUTHENTIFICATION.
builder.AddHbaSecurity();

// LE SOCLE PARTAGÉ MANQUAIT, ET LE PROCESSUS NE DÉMARRAIT PAS.
builder.Services.AddBuildingBlocksInfrastructure(builder.Configuration);

builder.Services.AddDeliveryPricingInfrastructure(builder.Configuration);

// SANS CECI, `/health/ready` RENDAIT `Ok` QUOI QU'IL ARRIVE.
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<DeliveryPricingDbContext>("database", tags: ["ready"]);

builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieDeliveryPricing();

var app = builder.Build();

app.UseHbaSecurity();

// `live` RESTE UNE CONSTANTE, ET C'EST CORRECT : la vivacité répond « le processus
// tourne », rien d'autre.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

// La disponibilité, elle, dépend de la base : sans elle, aucun devis ne peut être
// ni établi, ni relu, ni consommé.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapDeliveryPricingEndpoints();

app.MapInternalGrpcService<DeliveryPricingGrpcService>();

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
await app.MigrateHbaDatabaseAsync<DeliveryPricingDbContext>();

if (app.SortirApresMigrations())
{
    return;
}

app.Run();

public partial class Program
{
}
