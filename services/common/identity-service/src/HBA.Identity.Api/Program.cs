using HBA.Identity.Api;
using HBA.Identity.Api.Endpoints;
using HBA.Identity.Contracts.Grpc;
using HBA.Identity.Infrastructure;
using HBA.Identity.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<IdentityDbContext>(new IdentityModuleInstaller());
builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

// ═════════════════════════════════════════════════════════════════════════
// CE SERVICE SERT `/api/v1/auth/*` — ET CE COMMENTAIRE A DIT LE CONTRAIRE
//    PENDANT TOUTE LA DURÉE DE LA PANNE (ISSUE-063).
//
// Il affirmait : « LES CHEMINS PUBLICS RESTENT `/api/identity/*`, PAS
// `/api/auth/*` […] la passerelle enverrait `/api/identity/auth/login` à un
// service n'écoutant plus que `/api/auth/login`, et l'appel finirait en 404 ».
//
// Le raisonnement était juste. Sa PRÉMISSE a cessé de l'être : le renommage a eu
// lieu quand même, vers `/api/v1/auth`, et le commentaire n'a pas suivi. La
// passerelle a continué de réécrire `/api/auth/*` en `/api/identity/auth/*` —
// un préfixe que ce service n'a jamais servi. TOUTE la surface publique
// d'authentification rendait 404 : login, register, refresh, logout,
// password/forgot, password/reset, email/verify, confirm-email, reauthenticate.
//
// Le cluster était bon, la destination joignable, le service en bonne santé.
// Rien ne ressemblait à une panne.
//
// LES QUATRE GROUPES RÉELLEMENT SERVIS, pour que la prochaine réécriture
// puisse être vérifiée sans lire tout le fichier :
//
//     /api/v1/auth          — register, confirm-email, login, refresh,
//                             reauthenticate, logout, otp/request, verify-otp,
//                             password/forgot, password/reset, email/resend,
//                             email/verify
//     /api/identity/account — le compte de l'utilisateur connecté
//     /api/identity/users   — administration des comptes
//     /api/identity/roles   — administration des rôles
// ═════════════════════════════════════════════════════════════════════════
app.MapIdentityEndpoints();

app.MapInternalGrpcService<IdentityGrpcService>();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<IdentityDbContext>();

// APRÈS LES MIGRATIONS, ET C'EST UN ORDRE, PAS UNE PRÉFÉRENCE.
//
// L'amorçage écrit dans `roles` et `users`. Sur une base neuve, l'inverser
// donnerait « relation "identity.roles" does not exist » — une erreur qui
// désigne le semis alors que la faute est à l'ordre des deux lignes.
await app.SeedIdentityAsync();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
