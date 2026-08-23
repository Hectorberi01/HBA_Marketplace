using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Caching;
using HBA.Shared.Infrastructure.Outbox;

namespace HBA.Engagement.Reviews.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF.</summary>
public sealed class ReviewsDbContextFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REVIEWS_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ReviewsDbContext.SchemaName))
            .Options;

        return new ReviewsDbContext(
            options,
            NoOpDomainEventDispatcher.Instance,
            new IntegrationEventQueue(),
            NoOpCacheService.Instance);
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
