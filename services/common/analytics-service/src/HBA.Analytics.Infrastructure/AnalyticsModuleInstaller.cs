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
/// <remarks>
/// CE QUE CET INSTALLEUR N'ENREGISTRE PAS, ET IL FAUT LE SAVOIR : ni l'inbox, ni
/// les abonnements, ni les gestionnaires d'evenements. Ils sont dans
/// `AjouterMessagerieAnalytics()`, que le composition root peut oublier — d'ou la
/// `GardeDeCablage` ci-dessous, enregistree ICI parce qu'elle doit exister quand
/// ce qu'elle verifie est absent.
///
/// PAS DE VALIDATEURS. Ce service n'expose aucune commande : ses trois routes
/// sont des lectures dont les bornes sont verifiees par `PeriodeDemandee`, qui
/// rend un `Result` plutot qu'un `ValidationException`. Appeler
/// `AddValidatorsFromAssembly` sur un assemblage sans validateur ne coute rien et
/// ne dit rien ; l'omettre dit qu'il n'y en a pas.
/// </remarks>
public sealed class AnalyticsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Analytics";

    public Assembly ApplicationAssembly => typeof(GetSellerSalesSeriesQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LES SONDES DE CE SERVICE (Observability/). Sans la sonde du courtier,
        // un service dont le consommateur Kafka est mort repond « ready » : ses
        // graphes se figent, et le deploiement individuel le croit sain.
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
        //
        // En singleton il capturerait un `IRegistreDesRollUps` de la premiere
        // portee, donc un `DbContext` ferme — l'erreur classique de la dependance
        // captive, qui ne se manifeste qu'au DEUXIEME message consomme.
        services.AddScoped<ProjecteurDeRollUps>();
    }
}
