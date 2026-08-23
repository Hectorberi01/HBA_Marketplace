using HBA.Shared.Hosting;
using HBA.Tracking.Api.Endpoints;
using HBA.Tracking.Api.Grpc;
using HBA.Tracking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ═════════════════════════════════════════════════════════════════════════
// CE SERVICE N'AVAIT AUCUNE AUTHENTIFICATION.
//
// Ni `UseAuthentication`, ni `UseAuthorization` : toute sa surface était
// publique. Le fait qu'aucune route de la passerelle n'y mène aujourd'hui
// n'est pas un contrôle — c'est une coïncidence de déploiement.
//
// `AddHbaSecurity` pose le JWT et la politique de repli « au moins un
// compte ». Ce n'est pas le socle complet : ce service n'a pas de base à
// sonder ni de domaine à câbler. Le jour où il en aura une, il devra
// passer à `AddHbaService<TDbContext>`.
// ═════════════════════════════════════════════════════════════════════════
builder.AddHbaSecurity();

builder.Services.AddTrackingInfrastructure();
builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaSecurity();

app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "HBA.Tracking" })).AllowAnonymous();
// SONDE CONSTANTE : ELLE NE PEUT PAS ÉCHOUER (lot 9.5).
//
// `ready` répond « prêt » quoi qu'il arrive. C'est toléré ICI, et seulement ici,
// parce que ce service est un SQUELETTE : son magasin est en mémoire, il n'a
// aucune dépendance à sonder, et son README le dit en bandeau.
//
// LE JOUR OÙ IL AURA UNE BASE, CETTE LIGNE DEVIENDRA UN MENSONGE — une sonde
// qui affirme au lieu de vérifier laisse l'instance en rotation alors qu'elle ne
// peut plus rien servir. C'est exactement ce qui s'était produit sur
// delivery-pricing-service, dont l'encadré affirmait « pas de base à sonder »
// alors qu'il en avait une depuis le lot 0.4.
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "HBA.Tracking" })).AllowAnonymous();

app.MapTrackingEndpoints();
app.MapInternalGrpcService<TrackingGrpcService>();

app.Run();

public partial class Program
{
}
