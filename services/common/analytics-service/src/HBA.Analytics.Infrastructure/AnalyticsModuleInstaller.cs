using System.Reflection;
using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Application.RollUps.Queries;
using HBA.Analytics.Domain.RollUps;
using HBA.Analytics.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Analytics.Infrastructure.Observability;
using HBA.Analytics.Infrastructure.Persistence;
using HBA.Analytics.Infrastructure.Persistence.Repositories;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure;

/// <summary>
/// Enregistre le module Analytics : DbContext, registre des roll-ups, projecteur,
/// sondes.
/// </summary>
public sealed class AnalyticsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Analytics";

    public Assembly ApplicationAssembly => typeof(GetSellerSalesSeriesQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteAnalytics(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<AnalyticsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AnalyticsDbContext.SchemaName)));

        services.AddScoped<IAnalyticsUnitOfWork>(sp => sp.GetRequiredService<AnalyticsDbContext>());

        services.AddScoped<IRegistreDesRollUps, RegistreDesRollUps>();

        // LE PROJECTEUR EST `Scoped`, COMME LE REGISTRE QU'IL PORTE.
        services.AddScoped<ProjecteurDeRollUps>();
    }
}
