using HBA.Delivery.Pricing.Api.Endpoints;
using HBA.Delivery.Pricing.Api.GrpcServices;
using HBA.Delivery.Pricing.Infrastructure;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using HBA.Shared.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ═════════════════════════════════════════════════════════════════════════
// CE SERVICE N'AVAIT AUCUNE AUTHENTIFICATION.
//
// Ni `UseAuthentication`, ni `UseAuthorization` : toute sa surface était
// publique. Le fait qu'aucune route de la passerelle n'y mène aujourd'hui
// n'est pas un contrôle — c'est une coïncidence de déploiement.
//
// `AddHbaSecurity` pose le JWT et la politique de repli « au moins un
// compte ».
//
// LA SUITE DE CET ENCADRÉ DISAIT « CE SERVICE N'A PAS DE BASE À SONDER ».
// C'ÉTAIT FAUX, ET LA SONDE MENTAIT EN CONSÉQUENCE (lot 9.5).
//
// Il en a une depuis le lot 0.4 : `DeliveryPricingDbContext`, le schéma
// `delivery_pricing`, sa migration initiale, et `EfDeliveryPricingStore` qui
// écrit chaque devis. « Le jour où il en aura une » était déjà arrivé quand
// cette phrase a été écrite.
//
// Il n'adopte pas `AddHbaService<TDbContext>` pour autant : celui-ci exige un
// `IModuleInstaller`, du MediatR et une couche Application que ce service n'a
// pas. On prend donc la seule chose qui manquait — la sonde réelle.
// ═════════════════════════════════════════════════════════════════════════
builder.AddHbaSecurity();

builder.Services.AddDeliveryPricingInfrastructure(builder.Configuration);

// SANS CECI, `/health/ready` RENDAIT `Ok` QUOI QU'IL ARRIVE.
//
// Une sonde qui ne peut pas échouer est pire qu'une sonde absente : elle
// AFFIRME. Base injoignable, l'orchestrateur laisse l'instance en rotation, et
// chaque appel échoue en 500 au lieu que le trafic soit détourné.
//
// Et ce service est sur le chemin critique de CHAQUE passage en caisse depuis
// que la relecture de devis lui a été branchée : `LookupQuote` est appelé par
// le checkout marchandise ET par le checkout repas, où le devis est obligatoire.
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<DeliveryPricingDbContext>("database", tags: ["ready"]);

builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaSecurity();

// `live` RESTE UNE CONSTANTE, ET C'EST CORRECT : la vivacité répond
// « le processus tourne », rien d'autre. Y sonder la base ferait REDÉMARRER le
// conteneur à chaque hoquet de PostgreSQL — un service sain tué par une
// dépendance passagère.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

// La disponibilité, elle, dépend de la base : sans elle, aucun devis ne peut
// être ni établi, ni relu, ni consommé.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapDeliveryPricingEndpoints();

app.MapInternalGrpcService<DeliveryPricingGrpcService>();

app.Run();

public partial class Program
{
}
