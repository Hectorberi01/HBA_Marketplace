using HBA.Drivers.Api.Endpoints;
using HBA.Drivers.Api.Grpc;
using HBA.Drivers.Infrastructure;
using HBA.Drivers.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ═════════════════════════════════════════════════════════════════════════
// CE SERVICE PASSE DE `AddHbaSecurity` À `AddHbaService<TDbContext>`.
//
// L'ancien socle était le socle PARTIEL, écrit pour les hôtes sans base : il
// posait le JWT et la politique de repli, et rien d'autre. Le commentaire qu'il
// portait annonçait la suite — « le jour où il aura une base, il devra passer à
// `AddHbaService<TDbContext>` ». C'est ce jour (ISSUE-030).
//
// Ce que le socle complet apporte et que le partiel ne pouvait pas :
//   • `/health/ready` sonde réellement la base (`AddDbContextCheck`) ;
//   • MediatR ne scanne QUE l'assembly Application de ce module ;
//   • le pipeline de validation, de journalisation et d'observabilité ;
//   • l'inscription du module par `DriversModuleInstaller`, donc l'outbox.
// ═════════════════════════════════════════════════════════════════════════
builder.AddHbaService<DriverDbContext>(new DriversModuleInstaller());
builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService("driver-service", "DELIVERY_DRIVER");

app.MapDriverEndpoints();
app.MapInternalGrpcService<DriversGrpcService>();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup), comme
// pour delivery-service. En production, la migration est un geste de
// déploiement : un service qui migre en démarrant migre aussi quand un réplica
// redémarre au milieu d'un incident.
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<DriverDbContext>();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
