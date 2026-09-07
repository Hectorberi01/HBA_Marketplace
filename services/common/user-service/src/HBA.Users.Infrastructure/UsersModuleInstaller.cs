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
using HBA.Users.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Users.Infrastructure.Persistence;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Users.Infrastructure.Caching.Redis;
using HBA.Users.Infrastructure.Observability;
using HBA.Users.Infrastructure.Idempotency;
namespace HBA.Users.Infrastructure;

/// <summary>ENREGISTREMENT DU MODULE USER.</summary>
public sealed class UsersModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Users";

    public Assembly ApplicationAssembly => typeof(AddAddressCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheUsers(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteUsers(configuration);

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

        // L'OUTBOX ET L'INBOX SONT DESCENDUES DANS `Messaging/Kafka/`.
        services.AddHostedService<GardeDeCablage>();

        // L'IDEMPOTENCE RESTE ICI, ELLE, ET CE N'EST PAS UNE INCOHÉRENCE.
        services.AjouterIdempotenceUsers();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);
    }
}
