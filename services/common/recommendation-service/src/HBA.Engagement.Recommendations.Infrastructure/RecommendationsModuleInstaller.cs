using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Engagement.Recommendations.Application.Recommendations;
using HBA.Engagement.Recommendations.Domain.Recommendations;
using HBA.Engagement.Recommendations.Infrastructure.Persistence;

namespace HBA.Engagement.Recommendations.Infrastructure;

/// <summary>Enregistre le module Recommendations : DbContext read model, repository, outbox.</summary>
public sealed class RecommendationsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Recommendations";

    public Assembly ApplicationAssembly => typeof(UpsertRecommendationCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<RecommendationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", RecommendationsDbContext.SchemaName)));

        services.AddScoped<IRecommendationsUnitOfWork>(sp => sp.GetRequiredService<RecommendationsDbContext>());

        services.AddScoped<IRecommendationRepository, RecommendationRepository>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<RecommendationsDbContext>();
    }
}
