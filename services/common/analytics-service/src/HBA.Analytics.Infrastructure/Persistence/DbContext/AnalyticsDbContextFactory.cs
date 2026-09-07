using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HBA.Analytics.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF.</summary>
public sealed class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ANALYTICS_DB")
            ?? "Host=localhost;Port=5432;Database=analytics;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AnalyticsDbContext.SchemaName))
            .Options;

        return new AnalyticsDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
