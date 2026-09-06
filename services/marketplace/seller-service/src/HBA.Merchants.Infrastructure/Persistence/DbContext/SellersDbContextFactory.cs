using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Merchants.Infrastructure.Caching.Redis.Services;
using HBA.Merchants.Infrastructure.Persistence.Outbox;
using HBA.Merchants.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF (migrations add / database update).</summary>
public sealed class SellersDbContextFactory : IDesignTimeDbContextFactory<SellersDbContext>
{
    public SellersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SELLERS_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<SellersDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SellersDbContext.SchemaName))
            .Options;

        return new SellersDbContext(
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
