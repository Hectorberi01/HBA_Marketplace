using System.Reflection;
using FluentValidation;
using HBA.Users.Application.Abstractions;
using HBA.Users.Application.Addresses;
using HBA.Users.Domain.Addresses;
using HBA.Users.Domain.Devices;
using HBA.Users.Domain.Preferences;
using HBA.Users.Domain.Profiles;
using HBA.Users.Contracts;
using HBA.Users.Infrastructure.Public;
using HBA.Users.Infrastructure.Persistence;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Users.Infrastructure;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ENREGISTREMENT DU MODULE USER.
///
/// Le module ne porte pour l'instant qu'un domaine : le CARNET D'ADRESSES, repris
/// d'Identity. Le profil, l'avatar et les préférences prévus au cahier
/// d'architecture viendront s'ajouter ici, sans nouvel installer.
///
/// AUCUNE DÉPENDANCE VERS IDENTITY. Le module reçoit un <c>UserId</c> et le
/// traite comme une référence opaque : il ne le résout pas, ne le valide pas
/// contre la table des comptes, et n'a pas de quoi le faire. C'est cette absence
/// de lien qui permet d'extraire le module plus tard sans migration de données.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UsersModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Users";

    public Assembly ApplicationAssembly => typeof(AddAddressCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<UsersDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", UsersDbContext.SchemaName)));

        services.AddScoped<IUsersUnitOfWork>(sp => sp.GetRequiredService<UsersDbContext>());

        services.AddScoped<IAddressRepository, AddressRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
        services.AddScoped<IUserDeviceRepository, UserDeviceRepository>();
        services.AddScoped<IUsersModuleApi, UsersModuleApi>();

        // Socle du §5 et du §19.5. Sans ces deux enregistrements, le filtre
        // d'idempotence laisse passer en journalisant une erreur et les consumers
        // n'ont aucune garde contre le double traitement : les tables existent, et
        // rien ne s'en sert.
        services.AddScoped<IConsumerInbox, EfConsumerInbox<UsersDbContext>>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<UsersDbContext>>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        // Le module n'émet encore aucun événement d'intégration, mais la table outbox
        // existe (elle vient de ModuleDbContext) et le processeur doit tourner dès
        // maintenant : le jour où « AddressAdded » sera publié, rien à rebrancher.
        services.AddOutboxProcessor<UsersDbContext>();
    }
}
