using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Engagement.Recommendations.Application.Recommendations;
using HBA.Engagement.Recommendations.Domain.Recommendations;

namespace HBA.Engagement.Recommendations.Infrastructure.Persistence;

/// <summary>DbContext du module Recommendations (schéma « recommendations »).</summary>
public sealed class RecommendationsDbContext : ModuleDbContext, IRecommendationsUnitOfWork
{
    public const string SchemaName = "recommendations";

    public RecommendationsDbContext(
        DbContextOptions<RecommendationsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Recommendation> Recommendations => Set<Recommendation>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecommendationsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
