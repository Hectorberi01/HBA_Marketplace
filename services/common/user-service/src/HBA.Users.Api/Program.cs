using HBA.Identity.Contracts.Grpc;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Api.Endpoints;
using HBA.Users.Api.Integration;
using HBA.Shared.Hosting;
using HBA.Users.Contracts.Grpc;
using HBA.Users.Infrastructure;
using HBA.Users.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<UsersDbContext>(new UsersModuleInstaller());
builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// LE PONT IDENTITY → USER, ET IL N'EXISTE QU'ICI.
//
// IL AVAIT ÉTÉ PERDU À L'EXTRACTION.
//
// identity-service publiait `UserRegisteredIntegrationEvent` et personne ne
// l'écoutait : un compte se créait dans `identity.users`, aucune ligne
// n'apparaissait dans `users.profiles`. Aucune erreur, aucun journal — un
// événement sans destinataire ne se plaint pas.
//
// L'enregistrement est fait dans le composition root, PAS dans
// `UsersModuleInstaller` : celui-ci vit dans Infrastructure, à qui
// `UsersBoundaryTests` interdit de connaître Identity.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddIdentityGrpcClient(builder.Configuration);

// Inscription → création du profil.
builder.Services.AddScoped<
    IIntegrationEventHandler<UserRegisteredIntegrationEvent>,
    CreateUserProfileOnUserRegisteredHandler>();

// Le compte change de nom → le profil suit.
builder.Services.AddScoped<
    IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>,
    RenameUserProfileOnIdentityProfileUpdatedHandler>();

// CELUI-CI EST UNE OBLIGATION LÉGALE, PAS UN CONFORT.
//
// Sans lui, un compte supprimé laisse le carnet d'adresses de son titulaire en
// base, indéfiniment, sans que rien ne signale qu'il aurait dû partir.
builder.Services.AddScoped<
    IIntegrationEventHandler<UserAnonymizedIntegrationEvent>,
    PurgeUserDataOnAccountAnonymizedHandler>();

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
