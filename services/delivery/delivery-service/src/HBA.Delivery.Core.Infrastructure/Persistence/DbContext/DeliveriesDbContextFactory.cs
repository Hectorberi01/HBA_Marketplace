using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using HBA.Deliveries.Infrastructure.Persistence.Outbox;
using HBA.Deliveries.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Deliveries.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF (`dotnet ef migrations add`).</summary>
public sealed class DeliveriesDbContextFactory : IDesignTimeDbContextFactory<DeliveriesDbContext>
{
    public DeliveriesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DELIVERIES_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DeliveriesDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveriesDbContext.SchemaName))
            .Options;

        return new DeliveriesDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
