using System.Reflection;
using HBA.Drivers.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Delivery.Driver.Domain.Events;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Drivers.Application.Abstractions;
using HBA.Drivers.Application.Accounts.Commands;
using HBA.Drivers.Application.Accounts.Events;
using HBA.Drivers.Infrastructure.Persistence;
using HBA.Drivers.Infrastructure.Persistence.Repositories;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Drivers.Infrastructure.Caching.Redis;
using HBA.Drivers.Infrastructure.Observability;
namespace HBA.Drivers.Infrastructure;

/// <summary>L'INSTALLEUR DU MODULE Drivers — IL REMPLACE `DriversInfrastructureModule`.</summary>
public sealed class DriversModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Drivers";

    public Assembly ApplicationAssembly => typeof(RegisterDriverCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheDeliveryDriver(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteDeliveryDriver(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieDeliveryDriver()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<DriverDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DriverDbContext.SchemaName)));

        services.AddScoped<IDriverAccountRepository, DriverAccountRepository>();
        services.AddScoped<IDriverUnitOfWork, DriverUnitOfWork>();

        // NI `IntegrationEventQueue` NI `IIntegrationEventPublisher` NE SONT
        // ENREGISTRÉS ICI, ET C'EST VOLONTAIRE.

        // SANS CES QUATRE LIGNES, LES ÉVÉNEMENTS DU MODULE NE SORTENT PAS.
        services.AddScoped<IDomainEventHandler<DriverAccountRegisteredDomainEvent>, DriverRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DriverAccountVerifiedDomainEvent>, DriverVerifiedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DriverAccountSuspendedDomainEvent>, DriverSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DriverVehicleDeclaredDomainEvent>, DriverVehicleDeclaredDomainEventHandler>();

        // LA LIGNE QUE LE LOT 5.4 AVAIT ANNONCÉE ICI — ISSUE-007, CRITICAL.
    }
}
