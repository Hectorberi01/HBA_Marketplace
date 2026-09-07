using System.Reflection;
using HBA.Engagement.Recommendations.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Engagement.Recommendations.Application.Recommendations;
using HBA.Engagement.Recommendations.Domain.Recommendations;
using HBA.Engagement.Recommendations.Infrastructure.Persistence;

using HBA.Engagement.Recommendations.Infrastructure.Caching.Redis;
using HBA.Engagement.Recommendations.Infrastructure.Observability;
namespace HBA.Engagement.Recommendations.Infrastructure;

/// <summary>
/// Enregistre le module Recommendations : DbContext read model, repository, outbox.
/// </summary>
public sealed class RecommendationsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Recommendations";

    public Assembly ApplicationAssembly => typeof(UpsertRecommendationCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheEngagementRecommendations(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteEngagementRecommendations(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieEngagementRecommendations()`, que le composition root
        // peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<RecommendationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", RecommendationsDbContext.SchemaName)));

        services.AddScoped<IRecommendationsUnitOfWork>(sp => sp.GetRequiredService<RecommendationsDbContext>());

        services.AddScoped<IRecommendationRepository, RecommendationRepository>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
