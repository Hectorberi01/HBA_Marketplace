using HBA.Users.Api.Endpoints;
using HBA.Users.Infrastructure.Messaging.Kafka;
using HBA.Shared.Hosting;
using HBA.Users.Infrastructure;
using HBA.Users.Infrastructure.Persistence;

using HBA.Users.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<UsersDbContext>(new UsersModuleInstaller());
builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// LE CLIENT gRPC VERS identity-service A ÉTÉ RETIRÉ.
//
// Il servait UN SEUL APPEL : relire le compte pour obtenir le nom de famille,
// que `UserRegisteredIntegrationEvent` ne portait pas. Cet appel synchrone,
// fait depuis un consommateur Kafka, rendait ce service dépendant de la
// disponibilité — et de la clé de signature — d'un autre.
//
// CE QU'IL A COÛTÉ. `Internal:PrivateKey` en SEC1 au lieu de PKCS#8 : signature
// impossible, appel impossible, profil jamais créé, et aucune trace ailleurs
// que dans un journal de consommateur. L'événement porte désormais le champ.
//
// CE QUE ÇA CHANGE POUR LE DÉPLOIEMENT. user-service n'ouvre plus aucun canal
// sortant vers identity-service : il se déploie, démarre et traite ses
// événements sans lui. Il SERT toujours du gRPC (`UsersGrpcService`), et a donc
// toujours besoin de ses clés — mais pour VÉRIFIER des appels entrants, pas
// pour en signer.
//
// CE QUE ÇA NE CHANGE PAS. Le projet référence encore
// `HBA.Identity.Contracts` : les types d'événements qu'il consomme y vivent.
// Un contrat partagé n'est pas un appel réseau.
// ═════════════════════════════════════════════════════════════════════════

// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ÉCOUTE EST DÉCLARÉ DANS SON PROPRE MODULE.
//
// Les trois enregistrements vivaient ici, et la liste des sujets nulle part —
// le service s'abonnait donc aux vingt sujets de la plateforme pour en traiter
// trois. `HBA.Users.Infrastructure/Messaging/Kafka/` porte désormais les deux,
// côte à côte : un gestionnaire dont le sujet n'est pas déclaré ne serait jamais
// appelé, en silence, et rien d'autre ne relie les deux.
//
// CET APPEL N'EST PLUS FACULTATIF. Il porte aussi l'outbox et l'inbox du
// service : l'oublier laisserait un service qui démarre et n'émet plus rien.
// `GardeDeCablage`, enregistrée par l'installeur, refuse le démarrage dans ce
// cas — c'est la contrepartie du déplacement.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessagerieUsers();

var app = builder.Build();

app.UseHbaService();

app.MapUserEndpoints();

app.MapInternalGrpcService<UsersGrpcService>();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<UsersDbContext>();

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
