using HBA.Delivery.Pricing.Api.Endpoints;
using HBA.Delivery.Pricing.Api.GrpcServices;
using HBA.Delivery.Pricing.Infrastructure;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using HBA.Shared.Hosting;
using HBA.Shared.Infrastructure;
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

// ═════════════════════════════════════════════════════════════════════════
// LE SOCLE PARTAGÉ MANQUAIT, ET LE PROCESSUS NE DÉMARRAIT PAS.
//
// L'encadré ci-dessus explique pourquoi ce service n'adopte pas
// `AddHbaService<TDbContext>` : il n'a ni `IModuleInstaller`, ni MediatR, ni
// couche Application. C'est toujours vrai. Mais `AddHbaService` faisait DEUX
// choses, et seule la première ne s'applique pas ici : il installe un module,
// et il pose le socle d'infrastructure. En renonçant au premier, ce service
// avait perdu le second sans que rien ne le dise.
//
// CE QUI MANQUAIT, ET CE QUE ÇA DONNAIT :
//
//   • `IDomainEventDispatcher` — `DeliveryPricingDbContext` dérive de
//     `ModuleDbContext`, dont le constructeur l'exige. Le conteneur refusait
//     donc de construire le DbContext, et la validation au démarrage levait.
//   • `IOutboxMetrics` — exigé par `OutboxProcessor<T>`, monté juste en
//     dessous par `AddOutboxProcessor`. Même échec, sur le service hébergé.
//   • `IKafkaIntegrationEventPublisher` — celui-là ne se voyait PAS au
//     démarrage : `OutboxProcessor` le résout par lot, dans sa boucle de
//     fond. Le processus aurait donc démarré pour échouer toutes les cinq
//     secondes, en silence, sans qu'aucun devis ne soit publié.
//
// LA CONFIGURATION L'ATTENDAIT DÉJÀ. `docker-compose.dev.yml` donne à ce
// service `KAFKA__BOOTSTRAPSERVERS`, `KAFKA__CONSUMERGROUP`,
// `KAFKA__PRODUCER`, `REDIS__CONNECTIONSTRING` et la clé de protection des
// secrets — cinq réglages que seul le socle consomme. L'environnement
// décrivait une infrastructure que le processus ne câblait pas.
//
// CE QUE CET APPEL N'APPORTE PAS : ni MediatR, ni pipeline applicatif, ni
// installation de module. Le socle et `AddHbaService` restent deux choses
// distinctes, et ce service ne prend que la première.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddBuildingBlocksInfrastructure(builder.Configuration);

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

// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement. En production, le Job Kubernetes
// pose Database:MigrateOnly=true, applique les migrations puis sort avant
// `app.Run()`.
await app.MigrateHbaDatabaseAsync<DeliveryPricingDbContext>();

if (app.SortirApresMigrations())
{
    return;
}

app.Run();

public partial class Program
{
}
